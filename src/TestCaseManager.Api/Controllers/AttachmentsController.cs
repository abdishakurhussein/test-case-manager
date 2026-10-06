using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AttachmentsController(AppDbContext db) : ControllerBase
{
    private const int WorkbookLimit = 10 * 1024 * 1024;
    private const int ImageLimit = 5 * 1024 * 1024;
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("api/projects/{projectId:int}/workbooks")]
    public async Task<IActionResult> Workbooks(int projectId, CancellationToken token)
    {
        if (!await db.Projects.AnyAsync(item => item.Id == projectId, token)) return NotFound();
        var files = await db.StoredAttachments.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.Kind == "Workbook")
            .OrderByDescending(item => item.UploadedAt)
            .Select(item => new AttachmentSummary(item.Id, item.FileName, item.Caption, item.UploadedBy, item.UploadedAt, item.Data.Length))
            .ToListAsync(token);
        return Ok(files);
    }

    [HttpPost("api/projects/{projectId:int}/workbooks")]
    [RequestSizeLimit(WorkbookLimit + 1024 * 1024)]
    public async Task<IActionResult> UploadWorkbook(int projectId, IFormFile? file, [FromForm] string? caption, CancellationToken token)
    {
        if (!await db.Projects.AnyAsync(item => item.Id == projectId, token)) return NotFound();
        if (file is null || file.Length == 0 || file.Length > WorkbookLimit ||
            !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Detail = "Choose an .xlsx workbook up to 10 MB." });
        if (caption?.Length > 300) return BadRequest(new ProblemDetails { Detail = "Caption must be 300 characters or fewer." });
        var bytes = await Read(file, token);
        if (!IsXlsx(bytes)) return BadRequest(new ProblemDetails { Detail = "The file is not a valid .xlsx workbook." });
        var attachment = NewAttachment(file, bytes, "Workbook", Xlsx, caption);
        attachment.ProjectId = projectId;
        db.StoredAttachments.Add(attachment);
        await db.SaveChangesAsync(token);
        return Created($"/api/attachments/{attachment.Id}", Summary(attachment));
    }

    [HttpDelete("api/projects/{projectId:int}/workbooks/{id:int}")]
    public async Task<IActionResult> DeleteWorkbook(int projectId, int id, CancellationToken token)
    {
        var attachment = await db.StoredAttachments.SingleOrDefaultAsync(item =>
            item.Id == id && item.ProjectId == projectId && item.Kind == "Workbook", token);
        if (attachment is null) return NotFound();
        if (!User.IsInRole("Admin") &&
            !string.Equals(attachment.UploadedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        db.StoredAttachments.Remove(attachment);
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpGet("api/testcases/{caseId:int}/runs/{runId:int}/evidence")]
    public async Task<IActionResult> Evidence(int caseId, int runId, CancellationToken token)
    {
        if (!await db.ManualRuns.AnyAsync(run => run.Id == runId && run.TestCaseId == caseId, token)) return NotFound();
        var files = await db.StoredAttachments.AsNoTracking()
            .Where(item => item.Kind == "Evidence" && db.ManualStepResults.Any(step =>
                step.Id == item.ManualStepResultId && step.ManualRunId == runId))
            .OrderBy(item => item.UploadedAt)
            .Select(item => new EvidenceSummary(item.Id, item.ManualStepResultId!.Value, item.FileName,
                item.Caption, item.UploadedBy, item.UploadedAt, item.Data.Length))
            .ToListAsync(token);
        return Ok(files);
    }

    [HttpGet("api/testcases/{caseId:int}/evidence")]
    public async Task<IActionResult> CaseEvidence(int caseId, CancellationToken token)
    {
        if (!await db.TestCases.AnyAsync(item => item.Id == caseId, token)) return NotFound();
        var files = await db.StoredAttachments.AsNoTracking()
            .Where(item => item.Kind == "Evidence" && db.ManualStepResults.Any(step =>
                step.Id == item.ManualStepResultId && step.ManualRun.TestCaseId == caseId))
            .OrderBy(item => item.UploadedAt)
            .Select(item => new EvidenceSummary(item.Id, item.ManualStepResultId!.Value, item.FileName,
                item.Caption, item.UploadedBy, item.UploadedAt, item.Data.Length))
            .ToListAsync(token);
        return Ok(files);
    }

    [HttpPost("api/testcases/{caseId:int}/runs/{runId:int}/steps/{stepResultId:int}/evidence")]
    [RequestSizeLimit(ImageLimit + 1024 * 1024)]
    public async Task<IActionResult> UploadEvidence(int caseId, int runId, int stepResultId,
        IFormFile? file, [FromForm] string? caption, CancellationToken token)
    {
        var step = await db.ManualStepResults.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == stepResultId && item.ManualRunId == runId && item.ManualRun.TestCaseId == caseId, token);
        if (step is null) return NotFound();
        if (step.Outcome != "Failed") return Conflict(new ProblemDetails { Detail = "Evidence can be added to failed steps only." });
        if (file is null || file.Length == 0 || file.Length > ImageLimit)
            return BadRequest(new ProblemDetails { Detail = "Choose an image up to 5 MB." });
        if (caption?.Length > 300) return BadRequest(new ProblemDetails { Detail = "Caption must be 300 characters or fewer." });
        var bytes = await Read(file, token);
        var contentType = ImageType(bytes);
        if (contentType is null) return BadRequest(new ProblemDetails { Detail = "Only PNG, JPEG, and WebP images are accepted." });
        var attachment = NewAttachment(file, bytes, "Evidence", contentType, caption);
        attachment.ManualStepResultId = stepResultId;
        db.StoredAttachments.Add(attachment);
        await db.SaveChangesAsync(token);
        return Created($"/api/attachments/{attachment.Id}",
            new EvidenceSummary(attachment.Id, stepResultId, attachment.FileName, attachment.Caption,
                attachment.UploadedBy, attachment.UploadedAt, attachment.Data.Length));
    }

    [HttpGet("api/attachments/{id:int}")]
    public async Task<IActionResult> Download(int id, CancellationToken token)
    {
        var attachment = await db.StoredAttachments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
        if (attachment is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return attachment.Kind == "Evidence"
            ? File(attachment.Data, attachment.ContentType)
            : File(attachment.Data, attachment.ContentType, attachment.FileName);
    }

    private StoredAttachment NewAttachment(IFormFile file, byte[] bytes, string kind, string contentType, string? caption) => new()
    {
        Kind = kind, FileName = Path.GetFileName(file.FileName), ContentType = contentType,
        Caption = caption?.Trim() ?? "", UploadedBy = User.Identity?.Name ?? "Unknown",
        UploadedAt = DateTime.UtcNow, Data = bytes
    };

    private static AttachmentSummary Summary(StoredAttachment item) =>
        new(item.Id, item.FileName, item.Caption, item.UploadedBy, item.UploadedAt, item.Data.Length);

    private static async Task<byte[]> Read(IFormFile file, CancellationToken token)
    {
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, token);
        return stream.ToArray();
    }

    private static bool IsXlsx(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K') return false;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return zip.Entries.Count <= 1000 && zip.Entries.Sum(entry => entry.Length) <= 25 * 1024 * 1024 &&
                zip.GetEntry("[Content_Types].xml") is not null && zip.GetEntry("xl/workbook.xml") is not null &&
                !zip.Entries.Any(entry => entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { return false; }
    }

    private static string? ImageType(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public sealed record AttachmentSummary(int Id, string FileName, string Caption, string UploadedBy, DateTime UploadedAt, int Size);
    public sealed record EvidenceSummary(int Id, int StepResultId, string FileName, string Caption, string UploadedBy, DateTime UploadedAt, int Size);
}

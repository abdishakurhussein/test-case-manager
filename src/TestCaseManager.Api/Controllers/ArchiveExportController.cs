using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/archive")]
public class ArchiveExportController(AppDbContext db) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string format = "json", [FromQuery] int? projectId = null,
        CancellationToken token = default)
    {
        if (format is not ("json" or "csv"))
            return BadRequest(new ProblemDetails { Detail = "Use format=json or format=csv." });
        if (projectId is <= 0)
            return BadRequest(new ProblemDetails { Detail = "Project ID must be positive." });
        if (projectId.HasValue && !await db.Projects.AnyAsync(project => project.Id == projectId, token))
            return NotFound();

        var query = db.TestCases.AsNoTracking().Where(item => item.Status == TestCaseStatus.Archived);
        if (projectId.HasValue) query = query.Where(item => item.Module.ProjectId == projectId.Value);
        var cases = await query.Include(item => item.Module).ThenInclude(module => module.Project)
            .Include(item => item.Steps).OrderBy(item => item.Id).AsSplitQuery().ToListAsync(token);
        var ids = cases.Select(item => item.Id).ToArray();
        var runs = await db.ManualRuns.AsNoTracking().Include(run => run.Steps)
            .Where(run => ids.Contains(run.TestCaseId))
            .OrderBy(run => run.TestCaseId).ThenBy(run => run.CompletedAt).ThenBy(run => run.Id)
            .AsSplitQuery().ToListAsync(token);
        var runsByCase = runs.GroupBy(run => run.TestCaseId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var exportedAt = DateTime.UtcNow;
        var filename = $"archived-test-cases-{exportedAt:yyyyMMddTHHmmssZ}";
        if (format == "json")
        {
            var payload = new
            {
                schemaVersion = 1,
                exportedAtUtc = exportedAt,
                cases = cases.Select(item => new
                {
                    item.Id, item.Title, item.Description, item.Preconditions,
                    priority = item.Priority.ToString(),
                    statusBeforeArchive = item.StatusBeforeArchive?.ToString(),
                    createdAt = Utc(item.CreatedAt), updatedAt = Utc(item.UpdatedAt),
                    archivedAt = item.ArchivedAt is DateTime at ? Utc(at) : (DateTime?)null,
                    project = new { item.Module.Project.Id, item.Module.Project.Name },
                    module = new { item.Module.Id, item.Module.Name },
                    steps = item.Steps.OrderBy(step => step.Position).Select(step => new
                    {
                        step.Id, step.Position, step.Action, step.ExpectedResult
                    }),
                    runs = (runsByCase.GetValueOrDefault(item.Id) ?? new List<ManualRun>()).Select(run => new
                    {
                        run.Id, completedAt = Utc(run.CompletedAt), run.Result,
                        steps = run.Steps.OrderBy(step => step.Position).Select(step => new
                        {
                            step.OriginalStepId, step.Position, step.Action, step.ExpectedResult,
                            step.Outcome, step.ActualResult, step.CanReplicate, step.OnlyUserAffected
                        })
                    })
                })
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return File(bytes, "application/json", filename + ".json");
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteCsv(zip, "cases.csv",
                ["case_id", "project_id", "project_name", "module_id", "module_name", "title",
                 "description", "preconditions", "priority", "status_before_archive", "created_at_utc",
                 "updated_at_utc", "archived_at_utc"],
                cases.Select(item => new[] { item.Id.ToString(), item.Module.Project.Id.ToString(),
                    item.Module.Project.Name, item.Module.Id.ToString(), item.Module.Name, item.Title,
                    item.Description, item.Preconditions, item.Priority.ToString(),
                    item.StatusBeforeArchive?.ToString() ?? "", Iso(item.CreatedAt), Iso(item.UpdatedAt),
                    item.ArchivedAt is DateTime at ? Iso(at) : "" }));
            WriteCsv(zip, "case_steps.csv",
                ["case_id", "step_id", "position", "action", "expected_result"],
                cases.SelectMany(item => item.Steps.OrderBy(step => step.Position).Select(step => new[]
                { item.Id.ToString(), step.Id.ToString(), step.Position.ToString(), step.Action, step.ExpectedResult })));
            WriteCsv(zip, "runs.csv", ["case_id", "run_id", "completed_at_utc", "result"],
                runs.Select(run => new[] { run.TestCaseId.ToString(), run.Id.ToString(),
                    Iso(run.CompletedAt), run.Result }));
            WriteCsv(zip, "run_steps.csv",
                ["case_id", "run_id", "original_step_id", "position", "action", "expected_result",
                 "outcome", "actual_result", "can_replicate", "only_user_affected"],
                runs.SelectMany(run => run.Steps.OrderBy(step => step.Position).Select(step => new[]
                { run.TestCaseId.ToString(), run.Id.ToString(), step.OriginalStepId.ToString(),
                    step.Position.ToString(), step.Action, step.ExpectedResult, step.Outcome,
                    step.ActualResult ?? "", Answer(step.CanReplicate), Answer(step.OnlyUserAffected) })));
        }
        return File(stream.ToArray(), "application/zip", filename + ".zip");
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Iso(DateTime value) => Utc(value).ToString("O");
    private static string Answer(bool? value) => value is true ? "Yes" : value is false ? "No" : "Unknown";

    private static void WriteCsv(ZipArchive zip, string name, string[] header, IEnumerable<string[]> rows)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(',', header.Select(CsvCell)));
        foreach (var row in rows) writer.WriteLine(string.Join(',', row.Select(CsvCell)));
    }

    private static string CsvCell(string? raw)
    {
        var value = raw ?? "";
        // Excel treats user-authored values beginning with these characters as formulas.
        if (value.TrimStart(' ', '\t', '\r', '\n') is { Length: > 0 } trimmed &&
            trimmed[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

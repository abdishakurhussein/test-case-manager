using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ExcelExportController(AppDbContext db) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("api/projects/{projectId:int}/export")]
    public async Task<IActionResult> ProjectWorkbook(int projectId, [FromQuery] int? moduleId, CancellationToken token)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(item => item.Id == projectId, token);
        if (project is null) return NotFound();
        if (moduleId is <= 0 || (moduleId.HasValue && !await db.Modules.AnyAsync(item =>
            item.Id == moduleId && item.ProjectId == projectId, token))) return BadRequest(new ProblemDetails { Detail = "Choose a module in this project." });
        var query = db.TestCases.AsNoTracking()
            .Where(item => item.Module.ProjectId == projectId && item.Status != TestCaseStatus.Archived);
        if (moduleId.HasValue) query = query.Where(item => item.ModuleId == moduleId);
        var cases = await query
            .Include(item => item.Module).Include(item => item.Steps)
            .OrderBy(item => item.Module.Name).ThenBy(item => item.Id)
            .AsSplitQuery().ToListAsync(token);
        var instructions = new ExcelWorkbook.Sheet("Read me", ["ATCM offline workbook", "Guidance"], new List<string?[]>
        {
            new string?[] { "Project", project.Name },
            new string?[] { "Scope", moduleId.HasValue ? $"Module {moduleId}" : "All active project cases" },
            new string?[] { "Exported (UTC)", DateTime.UtcNow.ToString("u") },
            new string?[] { "Source of truth", "ATCM is the official record. Results typed here are not automatically saved to ATCM." },
            new string?[] { "Using this workbook", "Use the yellow cells for offline notes. Enter official results and evidence in ATCM." },
            new string?[] { "Case IDs", "Keep case and step IDs intact so colleagues can find the matching case in ATCM." }
        });
        var execution = new ExcelWorkbook.Sheet("Execution", ["Case ID", "Project", "Module", "Case title", "Priority", "Status", "Step ID", "Step", "Action", "Expected result", "Outcome", "Actual result / notes", "Tester", "Tested at"],
            cases.SelectMany(item => item.Steps.OrderBy(step => step.Position).Select(step => new string?[]
            {
                $"TC-{item.Id:000}", project.Name, item.Module.Name, item.Title, item.Priority.ToString(),
                item.Status.ToString(), step.Id.ToString(), step.Position.ToString(), step.Action, step.ExpectedResult,
                "Not run", "", "", ""
            })), [11, 12, 13, 14], 11);
        return File(ExcelWorkbook.Create(execution, instructions), Xlsx,
            $"atcm-project-{projectId}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("api/archive/excel")]
    public async Task<IActionResult> ArchiveWorkbook([FromQuery] int? projectId, CancellationToken token)
    {
        if (projectId is <= 0) return BadRequest();
        if (projectId.HasValue && !await db.Projects.AnyAsync(item => item.Id == projectId, token)) return NotFound();
        var query = db.TestCases.AsNoTracking().Where(item => item.Status == TestCaseStatus.Archived);
        if (projectId.HasValue) query = query.Where(item => item.Module.ProjectId == projectId);
        var cases = await query.Include(item => item.Module).ThenInclude(module => module.Project)
            .Include(item => item.Steps).OrderBy(item => item.Id).AsSplitQuery().ToListAsync(token);
        var ids = cases.Select(item => item.Id).ToArray();
        var runs = await db.ManualRuns.AsNoTracking().Include(run => run.Steps)
            .Where(run => ids.Contains(run.TestCaseId))
            .OrderBy(run => run.TestCaseId).ThenBy(run => run.CompletedAt).AsSplitQuery().ToListAsync(token);
        var stepLocations = runs.SelectMany(run => run.Steps.Select(step =>
                (StepResultId: step.Id, CaseId: run.TestCaseId, RunId: run.Id)))
            .ToDictionary(item => item.StepResultId, item => (item.CaseId, item.RunId));
        var resultIds = runs.SelectMany(run => run.Steps.Select(step => step.Id)).ToArray();
        var evidence = await db.StoredAttachments.AsNoTracking()
            .Where(item => item.Kind == "Evidence" && item.ManualStepResultId.HasValue &&
                resultIds.Contains(item.ManualStepResultId.Value))
            .OrderBy(item => item.Id)
            .Select(item => new { item.ManualStepResultId, item.FileName, item.Caption, item.UploadedBy, item.UploadedAt })
            .ToListAsync(token);
        var readme = new ExcelWorkbook.Sheet("Read me", ["ATCM archive", "Guidance"], new List<string?[]>
        {
            new string?[] { "Exported (UTC)", DateTime.UtcNow.ToString("u") },
            new string?[] { "Scope", projectId.HasValue ? $"Project {projectId}" : "All projects" },
            new string?[] { "Record", "This is an offline snapshot. ATCM remains the official record of archived cases and runs." }
        });
        var caseSheet = new ExcelWorkbook.Sheet("Cases", ["Case ID", "Project", "Module", "Title", "Description", "Preconditions", "Priority", "Status before archive", "Archived at (UTC)"],
            cases.Select(item => new string?[] { $"TC-{item.Id:000}", item.Module.Project.Name, item.Module.Name, item.Title,
                item.Description, item.Preconditions, item.Priority.ToString(), item.StatusBeforeArchive?.ToString(), item.ArchivedAt?.ToString("u") }));
        var stepSheet = new ExcelWorkbook.Sheet("Case steps", ["Case ID", "Step ID", "Position", "Action", "Expected result"],
            cases.SelectMany(item => item.Steps.OrderBy(step => step.Position).Select(step => new string?[]
                { $"TC-{item.Id:000}", step.Id.ToString(), step.Position.ToString(), step.Action, step.ExpectedResult })));
        var runSheet = new ExcelWorkbook.Sheet("Runs", ["Case ID", "Run ID", "Completed at (UTC)", "Result"],
            runs.Select(run => new string?[] { $"TC-{run.TestCaseId:000}", run.Id.ToString(), run.CompletedAt.ToString("u"), run.Result }));
        var resultSheet = new ExcelWorkbook.Sheet("Run steps", ["Case ID", "Run ID", "Original step ID", "Position", "Action", "Expected result", "Outcome", "Actual result", "Can replicate", "Only user affected"],
            runs.SelectMany(run => run.Steps.OrderBy(step => step.Position).Select(step => new string?[]
            {
                $"TC-{run.TestCaseId:000}", run.Id.ToString(), step.OriginalStepId.ToString(), step.Position.ToString(),
                step.Action, step.ExpectedResult, step.Outcome, step.ActualResult,
                Answer(step.CanReplicate), Answer(step.OnlyUserAffected)
            })));
        var evidenceSheet = new ExcelWorkbook.Sheet("Evidence", ["Case ID", "Run ID", "Step result ID", "File name", "Caption", "Uploaded by", "Uploaded at (UTC)"],
            evidence.Select(item =>
            {
                var location = stepLocations[item.ManualStepResultId!.Value];
                return new string?[] { $"TC-{location.CaseId:000}", location.RunId.ToString(),
                    item.ManualStepResultId.Value.ToString(), item.FileName, item.Caption, item.UploadedBy,
                    item.UploadedAt.ToString("u") };
            }));
        return File(ExcelWorkbook.Create(caseSheet, stepSheet, runSheet, resultSheet, evidenceSheet, readme), Xlsx,
            $"atcm-archive-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }

    private static string Answer(bool? value) => value is true ? "Yes" : value is false ? "No" : "Unknown";
}

using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Authorize]
public sealed class WorkbookImportController(AppDbContext db) : ControllerBase
{
    public sealed record CasePreview(string Module, string Title, int Steps, bool Exists);
    public sealed record ImportPreview(int ToCreate, int Skipped, string[] NewModules, CasePreview[] Cases);
    public sealed record ImportResult(int Created, int Skipped, int ModulesCreated);

    [HttpGet("api/projects/{projectId:int}/workbooks/{workbookId:int}/import-preview")]
    public async Task<IActionResult> Preview(int projectId, int workbookId, CancellationToken token)
    {
        var workbook = await db.StoredAttachments.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == workbookId && item.ProjectId == projectId && item.Kind == "Workbook", token);
        if (workbook is null) return NotFound();
        try
        {
            var definitions = WorkbookCaseImport.Parse(workbook.Data);
            var modules = await db.Modules.AsNoTracking().Where(item => item.ProjectId == projectId)
                .Select(item => new { item.Id, item.Name }).ToListAsync(token);
            var existing = await db.TestCases.AsNoTracking().Where(item => item.Module.ProjectId == projectId)
                .Select(item => new { item.Id, item.Title, item.ModuleId }).ToListAsync(token);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moduleIndex = modules.Select(module => (module.Id, module.Name)).ToList();
            var caseIndex = existing.Select(testCase => (testCase.Id, testCase.Title, testCase.ModuleId)).ToList();
            var previews = definitions.Select(item => new CasePreview(item.Module, item.Title, item.Steps.Count,
                Existing(item, moduleIndex, caseIndex) || !seen.Add(item.Module + "\u001f" + item.Title))).ToArray();
            var newModules = definitions.Where((_, index) => !previews[index].Exists)
                .Select(item => item.Module)
                .Where(name => !modules.Any(module => module.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return Ok(new ImportPreview(previews.Count(item => !item.Exists), previews.Count(item => item.Exists), newModules, previews));
        }
        catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException or ArgumentException)
        {
            return BadRequest(new ProblemDetails { Detail = error.Message });
        }
    }

    [HttpPost("api/projects/{projectId:int}/workbooks/{workbookId:int}/import")]
    public async Task<IActionResult> Import(int projectId, int workbookId, CancellationToken token)
    {
        var workbook = await db.StoredAttachments.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == workbookId && item.ProjectId == projectId && item.Kind == "Workbook", token);
        if (workbook is null) return NotFound();
        IReadOnlyList<WorkbookCaseImport.Case> definitions;
        try { definitions = WorkbookCaseImport.Parse(workbook.Data); }
        catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException or ArgumentException)
        { return BadRequest(new ProblemDetails { Detail = error.Message }); }

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var modules = await db.Modules.Where(item => item.ProjectId == projectId).ToListAsync(token);
        var existing = await db.TestCases.AsNoTracking().Where(item => item.Module.ProjectId == projectId)
            .Select(item => new { item.Id, item.Title, item.ModuleId }).ToListAsync(token);
        var created = 0;
        var modulesCreated = 0;
        foreach (var definition in definitions)
        {
            if (Existing(definition, modules.Select(module => (module.Id, module.Name)).ToList(),
                existing.Select(item => (item.Id, item.Title, item.ModuleId)).ToList())) continue;
            var module = modules.FirstOrDefault(item => item.Name.Equals(definition.Module, StringComparison.OrdinalIgnoreCase));
            if (module is null)
            {
                module = new Module { ProjectId = projectId, Name = definition.Module };
                db.Modules.Add(module);
                await db.SaveChangesAsync(token);
                modules.Add(module);
                modulesCreated++;
            }
            var imported = new TestCase
            {
                ModuleId = module.Id, Title = definition.Title, Description = definition.Description,
                Preconditions = definition.Preconditions, Priority = definition.Priority, Status = TestCaseStatus.Draft,
                Steps = definition.Steps.Select((step, index) => new TestStep
                { Position = index + 1, Action = step.Action, ExpectedResult = step.ExpectedResult }).ToList()
            };
            db.TestCases.Add(imported);
            await db.SaveChangesAsync(token);
            existing.Add(new { imported.Id, imported.Title, imported.ModuleId });
            created++;
        }
        await transaction.CommitAsync(token);
        return Ok(new ImportResult(created, definitions.Count - created, modulesCreated));
    }

    private static bool Existing(WorkbookCaseImport.Case definition, List<(int Id, string Name)> modules,
        List<(int Id, string Title, int ModuleId)> cases)
    {
        // Case IDs from another project must not suppress a new case in this project.
        if (definition.SourceId?.StartsWith("TC-", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(definition.SourceId.AsSpan(3), out var sourceId) &&
            cases.Any(item => item.Id == sourceId)) return true;
        var matchingModule = modules.FirstOrDefault(item => item.Name.Equals(definition.Module, StringComparison.OrdinalIgnoreCase));
        return matchingModule != default && cases.Any(item => item.ModuleId == matchingModule.Id &&
            item.Title.Equals(definition.Title, StringComparison.OrdinalIgnoreCase));
    }
}

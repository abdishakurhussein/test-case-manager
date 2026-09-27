using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/modules")]
public class ModulesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ModuleResponse>>> GetAll([FromQuery] int projectId, CancellationToken token)
    {
        if (!await db.Projects.AnyAsync(project => project.Id == projectId, token))
            return NotFound(new ProblemDetails { Title = "Project not found." });
        return await db.Modules.AsNoTracking().Where(module => module.ProjectId == projectId)
            .OrderBy(module => module.Name)
            .Select(module => new ModuleResponse(module.Id, module.ProjectId, module.Name, module.Description, module.TestCases.Count))
            .ToListAsync(token);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ModuleResponse>> GetById(int id, CancellationToken token)
    {
        var module = await db.Modules.AsNoTracking().Where(module => module.Id == id)
            .Select(module => new ModuleResponse(module.Id, module.ProjectId, module.Name, module.Description, module.TestCases.Count))
            .SingleOrDefaultAsync(token);
        return module is null ? NotFound() : Ok(module);
    }

    [HttpPost]
    public async Task<ActionResult<ModuleResponse>> Create(CreateModuleRequest request, CancellationToken token)
    {
        if (!await db.Projects.AnyAsync(project => project.Id == request.ProjectId, token))
            return NotFound(new ProblemDetails { Title = "Project not found." });
        var module = new Module { ProjectId = request.ProjectId, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "" };
        db.Modules.Add(module);
        await db.SaveChangesAsync(token);
        return CreatedAtAction(nameof(GetById), new { id = module.Id },
            new ModuleResponse(module.Id, module.ProjectId, module.Name, module.Description, 0));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var module = await db.Modules.FindAsync(new object?[] { id }, token);
        if (module is null)
            return NotFound();

        var hasTestCases = await db.TestCases
            .AnyAsync(testCase => testCase.ModuleId == id, token);

        if (hasTestCases)
            return Conflict (new ProblemDetails
            {
                Title = "Module cannot be deleted.",
                Detail = "Delete its test cases first."
            });

        db.Modules.Remove(module);
        await db.SaveChangesAsync(token);
        return NoContent();
    }
}

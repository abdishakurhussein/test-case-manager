using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ProjectResponse>>> GetAll(CancellationToken token) =>
        await db.Projects.AsNoTracking().OrderBy(project => project.Id)
            .Select(project => new ProjectResponse(project.Id, project.Name, project.Description,
                project.Modules.Count, project.Modules.SelectMany(module => module.TestCases).Count()))
            .ToListAsync(token);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectResponse>> GetById(int id, CancellationToken token)
    {
        var project = await db.Projects.AsNoTracking().Where(project => project.Id == id)
            .Select(project => new ProjectResponse(project.Id, project.Name, project.Description,
                project.Modules.Count, project.Modules.SelectMany(module => module.TestCases).Count()))
            .SingleOrDefaultAsync(token);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create(CreateProjectRequest request, CancellationToken token)
    {
        var project = new Project { Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "" };
        db.Projects.Add(project);
        await db.SaveChangesAsync(token);
        return CreatedAtAction(nameof(GetById), new { id = project.Id },
            new ProjectResponse(project.Id, project.Name, project.Description, 0, 0));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var project = await db.Projects.FindAsync(new object?[] { id }, token);
        if (project is null)
            return NotFound();

        var hasModules = await db.Modules
            .AnyAsync(module => module.ProjectId == id, token);

        if (hasModules)
            return Conflict(new ProblemDetails
            {
                Title = "Project cannot be deleted.",
                Detail = "Delete its modules first."
            });

        db.Projects.Remove(project);
        await db.SaveChangesAsync(token);
        return NoContent();
    }

}

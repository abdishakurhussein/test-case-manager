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
                project.Modules.Count, project.Modules.SelectMany(module => module.TestCases)
                    .Count(item => item.Status != TestCaseStatus.Archived),
                project.Modules.SelectMany(module => module.TestCases)
                    .Count(item => item.Status == TestCaseStatus.Archived)))
            .ToListAsync(token);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectResponse>> GetById(int id, CancellationToken token)
    {
        var project = await db.Projects.AsNoTracking().Where(project => project.Id == id)
            .Select(project => new ProjectResponse(project.Id, project.Name, project.Description,
                project.Modules.Count, project.Modules.SelectMany(module => module.TestCases)
                    .Count(item => item.Status != TestCaseStatus.Archived),
                project.Modules.SelectMany(module => module.TestCases)
                    .Count(item => item.Status == TestCaseStatus.Archived)))
            .SingleOrDefaultAsync(token);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpGet("{id:int}/overview")]
    public async Task<ActionResult<ProjectOverviewResponse>> GetOverview(int id, CancellationToken token)
    {
        if (!await db.Projects.AnyAsync(project => project.Id == id, token)) return NotFound();

        var allCases = db.TestCases.AsNoTracking().Where(item => item.Module.ProjectId == id);
        var archived = await allCases.CountAsync(item => item.Status == TestCaseStatus.Archived, token);
        var cases = allCases.Where(item => item.Status != TestCaseStatus.Archived);
        var states = await cases.GroupBy(_ => 1).Select(group => new
        {
            Total = group.Count(),
            Draft = group.Count(item => item.Status == TestCaseStatus.Draft),
            Ready = group.Count(item => item.Status == TestCaseStatus.Ready),
            Complete = group.Count(item => item.Status == TestCaseStatus.Complete)
        }).SingleOrDefaultAsync(token);

        // Count each case by its latest saved run, not by all historical runs.
        var latestResults = cases.Select(item => db.ManualRuns
            .Where(run => run.TestCaseId == item.Id)
            .OrderByDescending(run => run.CompletedAt).ThenByDescending(run => run.Id)
            .Select(run => run.Result).FirstOrDefault());
        var latestPassed = await latestResults.CountAsync(result => result == "Passed", token);
        var latestFailed = await latestResults.CountAsync(result => result == "Failed", token);
        var total = states?.Total ?? 0;
        return Ok(new ProjectOverviewResponse(total, states?.Draft ?? 0, states?.Ready ?? 0,
            states?.Complete ?? 0, archived, latestPassed, latestFailed,
            total - latestPassed - latestFailed));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create(CreateProjectRequest request, CancellationToken token)
    {
        var project = new Project { Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "" };
        db.Projects.Add(project);
        await db.SaveChangesAsync(token);
        return CreatedAtAction(nameof(GetById), new { id = project.Id },
            new ProjectResponse(project.Id, project.Name, project.Description, 0, 0, 0));
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

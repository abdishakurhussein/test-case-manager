using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/testcases")]
public class TestCasesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TestCaseSummary>>> GetAll(CancellationToken token) =>
        await db.TestCases.AsNoTracking().OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
            .Select(item => new TestCaseSummary(item.Id, item.Title, item.ModuleId, item.Module.Name,
                item.Module.ProjectId, item.Module.Project.Name, item.Priority.ToString(), item.Status.ToString(),
                DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc)))
            .ToListAsync(token);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TestCaseResponse>> GetById(int id, CancellationToken token)
    {
        var item = await db.TestCases.AsNoTracking()
            .Include(item => item.Module).ThenInclude(module => module.Project)
            .Include(item => item.Steps).SingleOrDefaultAsync(item => item.Id == id, token);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpPost]
    public async Task<ActionResult<TestCaseResponse>> Create(CreateTestCaseRequest request, CancellationToken token)
    {
        var module = await db.Modules.Include(module => module.Project)
            .SingleOrDefaultAsync(module => module.Id == request.ModuleId, token);
        if (module is null)
            return NotFound(new ProblemDetails { Title = "Module not found." });

        var item = new TestCase
        {
            Title = request.Title.Trim(), Description = request.Description?.Trim() ?? "",
            Preconditions = request.Preconditions?.Trim() ?? "", Module = module,
            Priority = request.Priority, Status = request.Status,
            // Derive positions on the server, so the form's order is authoritative.
            Steps = request.Steps.Select((step, index) => new TestStep
            {
                Position = index + 1, Action = step!.Action.Trim(), ExpectedResult = step.ExpectedResult.Trim()
            }).ToList()
        };
        db.TestCases.Add(item);
        // One relational SaveChanges transaction writes the case and every step.
        await db.SaveChangesAsync(token);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, ToResponse(item));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id,
        UpdateTestCaseStatusRequest request,
        CancellationToken token)
    {
        var item = await db.TestCases
            .SingleOrDefaultAsync(item => item.Id == id, token);

        if (item is null)
            return NotFound();

        if (item.Status == TestCaseStatus.Complete)
            return Conflict(new ProblemDetails { Detail = "A completed test case is read-only." });
        if (item.Status == TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "An archived test case cannot change status." });
        if (request.Status == TestCaseStatus.Complete)
        {
            if (item.Status != TestCaseStatus.Ready)
                return Conflict(new ProblemDetails { Detail = "Mark the test case Ready before completing it." });
            if (!await db.ManualRuns.AnyAsync(run => run.TestCaseId == id, token))
                return Conflict(new ProblemDetails { Detail = "Save at least one manual run before completing the test case." });
        }

        item.Status = request.Status!.Value;

        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPost("{id:int}/steps")]
    public async Task<ActionResult<TestCaseResponse>> AddStep(
        int id, CreateTestStepRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Action) || string.IsNullOrWhiteSpace(request.ExpectedResult))
            return BadRequest(new ProblemDetails { Detail = "Action and expected result are required." });

        var item = await db.TestCases
            .Include(testCase => testCase.Module).ThenInclude(module => module.Project)
            .Include(testCase => testCase.Steps)
            .SingleOrDefaultAsync(testCase => testCase.Id == id, token);
        if (item is null) return NotFound();
        if (item.Status == TestCaseStatus.Complete)
            return Conflict(new ProblemDetails { Detail = "A completed test case is read-only." });
        if (item.Steps.Count >= 100)
            return Conflict(new ProblemDetails { Detail = "A test case can contain at most 100 steps." });

        item.Steps.Add(new TestStep
        {
            Position = item.Steps.Max(step => step.Position) + 1,
            Action = request.Action.Trim(),
            ExpectedResult = request.ExpectedResult.Trim()
        });
        await db.SaveChangesAsync(token);
        return Ok(ToResponse(item));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var item = await db.TestCases.FindAsync(new object?[] { id }, token);
        if (item is null)
            return NotFound();

        // Keep the protective FK in place: deleting history is permitted only through
        // this deliberate case-deletion operation, and all changes commit together.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.ManualRuns.Where(run => run.TestCaseId == id).ExecuteDeleteAsync(token);
        db.TestCases.Remove(item);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return NoContent();
    }

    [HttpDelete("{id:int}/steps/{stepId:int}")]
    public async Task<IActionResult> DeleteStep(int id, int stepId, CancellationToken token)
    {
        var item = await db.TestCases.Include(testCase => testCase.Steps)
            .SingleOrDefaultAsync(testCase => testCase.Id == id, token);
        if (item is null) return NotFound();

        if (item.Status == TestCaseStatus.Complete)
            return Conflict(new ProblemDetails { Detail = "A completed test case is read-only." });

        var step = item.Steps.SingleOrDefault(candidate => candidate.Id == stepId);
        if (step is null) return NotFound();
        if (item.Steps.Count == 1)
            return Conflict(new ProblemDetails { Detail = "A test case must keep at least one step." });

        // Free the deleted position first, then shift higher positions down in order.
        // The transaction keeps the unique (TestCaseId, Position) index valid throughout.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        db.TestSteps.Remove(step);
        await db.SaveChangesAsync(token);
        foreach (var laterStep in item.Steps.Where(candidate => candidate.Position > step.Position)
                     .OrderBy(candidate => candidate.Position))
        {
            laterStep.Position--;
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return NoContent();
    }

    private static TestCaseResponse ToResponse(TestCase item) => new(
        item.Id, item.Title, item.Description, item.Preconditions, item.ModuleId, item.Module.Name,
        item.Module.ProjectId, item.Module.Project.Name, item.Priority.ToString(), item.Status.ToString(),
        DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc),
        item.Steps.OrderBy(step => step.Position)
            .Select(step => new TestStepResponse(step.Id, step.Position, step.Action, step.ExpectedResult)).ToList());
}

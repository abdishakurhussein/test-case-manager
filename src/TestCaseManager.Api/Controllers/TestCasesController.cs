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
        (await Summaries(db.TestCases.AsNoTracking().Where(item => item.Status != TestCaseStatus.Archived)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id)).ToListAsync(token)).Select(WithUtcDates).ToList();

    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<TestCaseSummary>>> Search(
        [FromQuery] int? projectId, [FromQuery] int? moduleId, [FromQuery] string? q,
        [FromQuery] string? status, [FromQuery] string? priority, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken token = default)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100 ||
            projectId is <= 0 || moduleId is <= 0 || q?.Length > 200)
            return BadRequest(new ProblemDetails { Detail = "Check the page, page size, IDs and search length." });
        if (sort is not null && sort is not ("updated-desc" or "updated-asc" or
            "title-asc" or "title-desc" or "id-asc"))
            return BadRequest(new ProblemDetails { Detail = "Unknown sort order." });

        var query = db.TestCases.AsNoTracking().AsQueryable();
        // Archived cases only appear when explicitly requested by the archive view.
        if (!string.Equals(status, nameof(TestCaseStatus.Archived), StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Status != TestCaseStatus.Archived);
        if (projectId.HasValue) query = query.Where(item => item.Module.ProjectId == projectId.Value);
        if (moduleId.HasValue) query = query.Where(item => item.ModuleId == moduleId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(item => item.Title.ToLower().Contains(term) ||
                item.Description.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TestCaseStatus>(status, true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
                return BadRequest(new ProblemDetails { Detail = "Unknown status filter." });
            query = query.Where(item => item.Status == parsedStatus);
        }
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<TestPriority>(priority, true, out var parsedPriority) ||
                !Enum.IsDefined(parsedPriority))
                return BadRequest(new ProblemDetails { Detail = "Unknown priority filter." });
            query = query.Where(item => item.Priority == parsedPriority);
        }

        var totalCount = await query.CountAsync(token);
        query = sort switch
        {
            null or "updated-desc" => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id),
            "updated-asc" => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.Id),
            "title-asc" => query.OrderBy(item => item.Title).ThenBy(item => item.Id),
            "title-desc" => query.OrderByDescending(item => item.Title).ThenByDescending(item => item.Id),
            "id-asc" => query.OrderBy(item => item.Id),
            _ => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
        };

        var items = (await Summaries(query.Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(token)).Select(WithUtcDates).ToList();
        return Ok(new PagedResult<TestCaseSummary>(items, totalCount, page, pageSize));
    }

    private IQueryable<TestCaseSummary> Summaries(IQueryable<TestCase> query) => query.Select(item =>
        new TestCaseSummary(item.Id, item.Title, item.ModuleId, item.Module.Name,
            item.Module.ProjectId, item.Module.Project.Name, item.Priority.ToString(), item.Status.ToString(),
            item.UpdatedAt,
            db.ManualRuns.Count(run => run.TestCaseId == item.Id),
            db.ManualRuns.Where(run => run.TestCaseId == item.Id)
                .OrderByDescending(run => run.CompletedAt).ThenByDescending(run => run.Id)
                .Select(run => run.Result).FirstOrDefault(),
            db.ManualRuns.Where(run => run.TestCaseId == item.Id)
                .OrderByDescending(run => run.CompletedAt).ThenByDescending(run => run.Id)
                .Select(run => (DateTime?)run.CompletedAt).FirstOrDefault(),
            item.ArchivedAt, item.StatusBeforeArchive.HasValue ? item.StatusBeforeArchive.Value.ToString() : null));

    private static TestCaseSummary WithUtcDates(TestCaseSummary item) => item with
    {
        UpdatedAt = DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc),
        LatestRunAt = item.LatestRunAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
        ArchivedAt = item.ArchivedAt is DateTime archived ? DateTime.SpecifyKind(archived, DateTimeKind.Utc) : null
    };

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

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TestCaseResponse>> Update(
        int id, UpdateTestCaseRequest request, CancellationToken token)
    {
        var item = await db.TestCases.Include(testCase => testCase.Module)
            .ThenInclude(module => module.Project).Include(testCase => testCase.Steps)
            .SingleOrDefaultAsync(testCase => testCase.Id == id, token);
        if (item is null) return NotFound();
        if (item.Status is TestCaseStatus.Complete or TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "Completed or archived test cases are view-only." });
        if (item.UpdatedAt != request.ExpectedUpdatedAt)
            return Conflict(new ProblemDetails { Detail = "This case changed since you opened it. Reload before editing again." });

        var existing = item.Steps.ToDictionary(step => step.Id);
        var requestedIds = request.Steps.Where(step => step!.Id.HasValue)
            .Select(step => step!.Id!.Value).ToHashSet();
        if (requestedIds.Any(stepId => !existing.ContainsKey(stepId)))
            return BadRequest(new ProblemDetails { Detail = "A step does not belong to this test case." });

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var removed = item.Steps.Where(step => !requestedIds.Contains(step.Id)).ToList();
        if (removed.Count > 0)
        {
            foreach (var step in removed) item.Steps.Remove(step);
            db.TestSteps.RemoveRange(removed);
            await db.SaveChangesAsync(token);
        }

        // Move stored positions out of the 1–100 range before assigning the new order.
        // This avoids collisions with the unique (TestCaseId, Position) index.
        await db.TestSteps.Where(step => step.TestCaseId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(step => step.Position,
                step => step.Position + 1000), token);

        // ExecuteUpdate bypasses tracking. Reload so EF sees the temporary positions
        // rather than inferring a circular 1↔2 swap from stale tracked values.
        db.ChangeTracker.Clear();
        item = await db.TestCases.Include(testCase => testCase.Module)
            .ThenInclude(module => module.Project).Include(testCase => testCase.Steps)
            .SingleAsync(testCase => testCase.Id == id, token);
        existing = item.Steps.ToDictionary(step => step.Id);

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.Preconditions = request.Preconditions?.Trim() ?? string.Empty;
        item.Priority = request.Priority;
        for (var index = 0; index < request.Steps.Count; index++)
        {
            var requested = request.Steps[index]!;
            if (requested.Id.HasValue)
            {
                var step = existing[requested.Id.Value];
                step.Position = index + 1;
                step.Action = requested.Action.Trim();
                step.ExpectedResult = requested.ExpectedResult.Trim();
            }
            else
            {
                item.Steps.Add(new TestStep
                {
                    Position = index + 1,
                    Action = requested.Action.Trim(),
                    ExpectedResult = requested.ExpectedResult.Trim()
                });
            }
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return Ok(ToResponse(item));
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

        if (item.Status is TestCaseStatus.Complete or TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "A completed or archived test case is read-only." });
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

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, CancellationToken token)
    {
        var item = await db.TestCases.FindAsync(new object?[] { id }, token);
        if (item is null) return NotFound();
        if (item.Status == TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "This case is already archived." });

        item.StatusBeforeArchive = item.Status;
        item.ArchivedAt = DateTime.UtcNow;
        item.Status = TestCaseStatus.Archived;
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id, CancellationToken token)
    {
        var item = await db.TestCases.FindAsync(new object?[] { id }, token);
        if (item is null) return NotFound();
        if (item.Status != TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "Only archived cases can be restored." });

        item.Status = item.StatusBeforeArchive is TestCaseStatus.Draft or TestCaseStatus.Ready or TestCaseStatus.Complete
            ? item.StatusBeforeArchive.Value : TestCaseStatus.Draft;
        item.StatusBeforeArchive = null;
        item.ArchivedAt = null;
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
        if (item.Status is TestCaseStatus.Complete or TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "A completed or archived test case is read-only." });
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

        if (item.Status is TestCaseStatus.Complete or TestCaseStatus.Archived)
            return Conflict(new ProblemDetails { Detail = "A completed or archived test case is read-only." });

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
            .Select(step => new TestStepResponse(step.Id, step.Position, step.Action, step.ExpectedResult)).ToList(),
        item.ArchivedAt is DateTime archived ? DateTime.SpecifyKind(archived, DateTimeKind.Utc) : null,
        item.StatusBeforeArchive?.ToString());
}

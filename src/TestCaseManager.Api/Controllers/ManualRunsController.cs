using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/testcases/{testCaseId:int}/runs")]
public class ManualRunsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ManualRunResponse>>> GetAll(int testCaseId, CancellationToken token)
    {
        if (!await db.TestCases.AnyAsync(item => item.Id == testCaseId, token))
            return NotFound();

        var runs = await db.ManualRuns.AsNoTracking().Include(run => run.Steps)
            .Where(run => run.TestCaseId == testCaseId)
            .OrderByDescending(run => run.CompletedAt).ThenByDescending(run => run.Id)
            .ToListAsync(token);
        return runs.Select(ToResponse).ToList();
    }

    [HttpGet("{runId:int}")]
    public async Task<ActionResult<ManualRunResponse>> GetById(int testCaseId, int runId, CancellationToken token)
    {
        var run = await db.ManualRuns.AsNoTracking().Include(item => item.Steps)
            .SingleOrDefaultAsync(item => item.TestCaseId == testCaseId && item.Id == runId, token);
        return run is null ? NotFound() : Ok(ToResponse(run));
    }

    [HttpPost]
    public async Task<ActionResult<ManualRunResponse>> Create(
        int testCaseId, SaveManualRunRequest request, CancellationToken token)
    {
        var item = await db.TestCases.AsNoTracking().Include(item => item.Steps)
            .SingleOrDefaultAsync(item => item.Id == testCaseId, token);
        if (item is null) return NotFound();
        if (item.Status != TestCaseStatus.Ready)
            return Conflict(new ProblemDetails { Detail = "Mark this test case Ready before saving a manual run." });

        var steps = item.Steps.OrderBy(step => step.Position).ToList();
        if (request.Steps is null || request.Steps.Any(step => step is null))
            return BadRequest(new ProblemDetails { Detail = "Every step needs a result." });
        var submitted = request.Steps.Select(step => step!).ToList();
        if (submitted.Count != steps.Count || submitted.Select(step => step.StepId).Distinct().Count() != steps.Count ||
            !submitted.All(step => steps.Any(saved => saved.Id == step.StepId)) ||
            !submitted.All(step => step.Outcome is "Passed" or "Failed"))
            return BadRequest(new ProblemDetails { Detail = "Record Passed or Failed exactly once for every current step." });

        if (submitted.Any(step => step.Outcome == "Failed" &&
            (string.IsNullOrWhiteSpace(step.ActualResult) ||
             (step.CanReplicate is null) != step.CanReplicateUnknown ||
             (step.OnlyUserAffected is null) != step.OnlyUserAffectedUnknown)))
            return BadRequest(new ProblemDetails { Detail = "Failed steps need an actual result and an explicit Yes, No or Unknown answer to both questions." });

        var outcomes = submitted.ToDictionary(step => step.StepId);
        var run = new ManualRun
        {
            TestCaseId = testCaseId,
            CompletedAt = DateTime.UtcNow,
            Result = submitted.Any(step => step.Outcome == "Failed") ? "Failed" : "Passed",
            Steps = steps.Select(step => new ManualStepResult
            {
                OriginalStepId = step.Id, Position = step.Position, Action = step.Action,
                ExpectedResult = step.ExpectedResult, Outcome = outcomes[step.Id].Outcome,
                ActualResult = outcomes[step.Id].Outcome == "Failed" ? outcomes[step.Id].ActualResult!.Trim() : null,
                CanReplicate = outcomes[step.Id].Outcome == "Failed" ? outcomes[step.Id].CanReplicate : null,
                OnlyUserAffected = outcomes[step.Id].Outcome == "Failed" ? outcomes[step.Id].OnlyUserAffected : null
            }).ToList()
        };
        db.ManualRuns.Add(run);
        await db.SaveChangesAsync(token);
        return CreatedAtAction(nameof(GetById), new { testCaseId, runId = run.Id }, ToResponse(run));
    }

    private static ManualRunResponse ToResponse(ManualRun run) => new(
        run.Id, run.TestCaseId, DateTime.SpecifyKind(run.CompletedAt, DateTimeKind.Utc), run.Result,
        run.Steps.OrderBy(step => step.Position)
            .Select(step => new ManualStepResultResponse(step.Id, step.OriginalStepId, step.Position,
                step.Action, step.ExpectedResult, step.Outcome, step.ActualResult,
                step.CanReplicate, step.OnlyUserAffected)).ToList());
}

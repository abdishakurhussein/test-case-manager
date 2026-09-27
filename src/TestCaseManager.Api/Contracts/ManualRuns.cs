using System.ComponentModel.DataAnnotations;

namespace TestCaseManager.Api.Contracts;

public class SaveManualStepRequest
{
    [Range(1, int.MaxValue)]
    public int StepId { get; set; }

    [Required]
    public string Outcome { get; set; } = string.Empty;
    [StringLength(4000)]
    public string? ActualResult { get; set; }
    public bool? CanReplicate { get; set; }
    public bool? OnlyUserAffected { get; set; }
}

public class SaveManualRunRequest
{
    [Required, MinLength(1)]
    public List<SaveManualStepRequest?> Steps { get; set; } = new();
}

public record ManualStepResultResponse(int OriginalStepId, int Position, string Action,
    string ExpectedResult, string Outcome, string? ActualResult,
    bool? CanReplicate, bool? OnlyUserAffected);

public record ManualRunResponse(int Id, int TestCaseId, DateTime CompletedAt, string Result,
    List<ManualStepResultResponse> Steps);

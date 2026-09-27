namespace TestCaseManager.Api.Models;

// Snapshots preserve what was tested even if the case's steps change later.
public class ManualStepResult
{
    public int Id { get; set; }
    public int ManualRunId { get; set; }
    public ManualRun ManualRun { get; set; } = null!;
    public int OriginalStepId { get; set; }
    public int Position { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ExpectedResult { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? ActualResult { get; set; }
    public bool? CanReplicate { get; set; }
    public bool? OnlyUserAffected { get; set; }
}

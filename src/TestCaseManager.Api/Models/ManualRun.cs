namespace TestCaseManager.Api.Models;

// A completed manual execution, separate from the reusable test-case definition.
public class ManualRun
{
    public int Id { get; set; }
    public int TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;
    public DateTime CompletedAt { get; set; }
    public string Result { get; set; } = string.Empty;
    public List<ManualStepResult> Steps { get; set; } = new();
}

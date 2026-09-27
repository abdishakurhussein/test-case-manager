using System.ComponentModel.DataAnnotations;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Contracts;

// DTOs accept only user-editable fields, never IDs or server timestamps.
public class CreateProjectRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [StringLength(2000)]
    public string? Description { get; set; }
}

public class CreateModuleRequest : CreateProjectRequest
{
    [Range(1, int.MaxValue)]
    public int ProjectId { get; set; }
}

public class CreateTestStepRequest
{
    [Required, StringLength(2000)]
    public string Action { get; set; } = string.Empty;
    [Required, StringLength(2000)]
    public string ExpectedResult { get; set; } = string.Empty;
}

public class CreateTestCaseRequest : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(4000)]
    public string? Description { get; set; }
    [StringLength(4000)]
    public string? Preconditions { get; set; }
    [Range(1, int.MaxValue)]
    public int ModuleId { get; set; }
    [EnumDataType(typeof(TestPriority))]
    public TestPriority Priority { get; set; } = TestPriority.Major;
    [EnumDataType(typeof(TestCaseStatus))]
    public TestCaseStatus Status { get; set; } = TestCaseStatus.Draft;
    [Required, MinLength(1), MaxLength(100)]
    public List<CreateTestStepRequest?> Steps { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is TestCaseStatus.Archived or TestCaseStatus.Complete)
            yield return new ValidationResult("Create a case as Draft or Ready.", new[] { nameof(Status) });
        if (Steps is not null && Steps.Any(step => step is null))
            yield return new ValidationResult("Every step must include an action and expected result.", new[] { nameof(Steps) });
    }
}

public class UpdateTestCaseStatusRequest : IValidatableObject
{
    [Required]
    [EnumDataType(typeof(TestCaseStatus))]
    public TestCaseStatus? Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == TestCaseStatus.Archived)
        {
            yield return new ValidationResult(
                "Change a case only to Draft, Ready, or Complete.",
                new[] { nameof(Status) });
        }
    }
}

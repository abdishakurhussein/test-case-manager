namespace TestCaseManager.Api.Contracts;

public record ProjectResponse(int Id, string Name, string Description, int ModuleCount, int TestCaseCount);
public record ModuleResponse(int Id, int ProjectId, string Name, string Description, int TestCaseCount);
public record TestCaseSummary(int Id, string Title, int ModuleId, string ModuleName,
    int ProjectId, string ProjectName, string Priority, string Status, DateTime UpdatedAt);
public record TestStepResponse(int Id, int Position, string Action, string ExpectedResult);
public record TestCaseResponse(int Id, string Title, string Description, string Preconditions,
    int ModuleId, string ModuleName, int ProjectId, string ProjectName, string Priority,
    string Status, DateTime CreatedAt, DateTime UpdatedAt, List<TestStepResponse> Steps);

namespace TestCaseManager.Api.Contracts;

public record ProjectResponse(int Id, string Name, string Description, int ModuleCount, int TestCaseCount, int ArchivedCaseCount);
public record ModuleResponse(int Id, int ProjectId, string Name, string Description, int TestCaseCount, int ArchivedCaseCount);
public record TestCaseSummary(int Id, string Title, int ModuleId, string ModuleName,
    int ProjectId, string ProjectName, string Priority, string Status, DateTime UpdatedAt,
    int RunCount, string? LatestRunResult, DateTime? LatestRunAt,
    DateTime? ArchivedAt, string? StatusBeforeArchive);
public record ProjectOverviewResponse(int TotalCases, int Draft, int Ready, int Complete,
    int Archived, int LatestPassed, int LatestFailed, int NeverRun);
public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);
public record TestStepResponse(int Id, int Position, string Action, string ExpectedResult);
public record TestCaseResponse(int Id, string Title, string Description, string Preconditions,
    int ModuleId, string ModuleName, int ProjectId, string ProjectName, string Priority,
    string Status, DateTime CreatedAt, DateTime UpdatedAt, List<TestStepResponse> Steps,
    DateTime? ArchivedAt, string? StatusBeforeArchive);

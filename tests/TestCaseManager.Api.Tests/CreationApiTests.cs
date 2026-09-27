using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;

namespace TestCaseManager.Api.Tests;

public class CreationApiTests
{
    // Each test gets a real HTTP test host and its own migrated SQLite file.
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tcm-http-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Storage:Directory", _directory);
        }
        public async Task<HttpClient> OpenAsync()
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            return client;
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (!Directory.Exists(_directory)) return;
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }

    private static async Task<ModuleResponse> CreateModuleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Portal", description = "Synthetic test project" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
        response = await client.PostAsJsonAsync("/api/modules", new { projectId = project.Id, name = "Login" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModuleResponse>())!;
    }

    private static JsonObject ValidRequest(int moduleId) => new()
    {
        ["moduleId"] = moduleId, ["title"] = "Valid login", ["status"] = "Ready", ["priority"] = "Major",
        ["steps"] = new JsonArray
        {
            new JsonObject { ["action"] = "Open login", ["expectedResult"] = "Form appears" },
            new JsonObject { ["action"] = "Submit credentials", ["expectedResult"] = "Account appears" }
        }
    };

    [Fact]
    public async Task CreateCase_ReturnsLocation_AndCanBeReadByAnotherRequest()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        var module = await CreateModuleAsync(client);
        var response = await client.PostAsJsonAsync("/api/testcases", ValidRequest(module.Id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = (await response.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var saved = await client.GetFromJsonAsync<TestCaseResponse>(response.Headers.Location);
        Assert.NotNull(saved);
        Assert.Equal(created.Id, saved.Id);
        Assert.Equal("Valid login", saved.Title);
        Assert.Equal(module.Id, saved.ModuleId);
        Assert.Equal("Portal", saved.ProjectName);
        Assert.Equal("Ready", saved.Status);
        Assert.Equal(new[] { 1, 2 }, saved.Steps.Select(step => step.Position));
        Assert.Equal("Form appears", saved.Steps[0].ExpectedResult);
        Assert.Equal("Submit credentials", saved.Steps[1].Action);
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAt.Kind);
        Assert.Equal(saved.CreatedAt, saved.UpdatedAt);
        Assert.Single((await client.GetFromJsonAsync<List<TestCaseSummary>>("/api/testcases"))!);
        var projects = (await client.GetFromJsonAsync<List<ProjectResponse>>("/api/projects"))!;
        Assert.Equal(1, projects.Single().ModuleCount);
        Assert.Equal(1, projects.Single().TestCaseCount);
    }

    [Theory]
    [InlineData("blank-title")]
    [InlineData("long-title")]
    [InlineData("no-steps")]
    [InlineData("null-steps")]
    [InlineData("null-step")]
    [InlineData("blank-action")]
    [InlineData("blank-expected")]
    [InlineData("bad-priority")]
    [InlineData("numeric-priority")]
    [InlineData("archived")]
    [InlineData("complete")]
    public async Task InvalidCase_Returns400_WithoutSavingPartialRecords(string scenario)
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        var module = await CreateModuleAsync(client);
        var request = ValidRequest(module.Id);
        switch (scenario)
        {
            case "blank-title": request["title"] = "   "; break;
            case "long-title": request["title"] = new string('x', 201); break;
            case "no-steps": request["steps"] = new JsonArray(); break;
            case "null-steps": request["steps"] = null; break;
            case "null-step": request["steps"] = new JsonArray((JsonNode?)null); break;
            case "blank-action": request["steps"]![0]!["action"] = "  "; break;
            case "blank-expected": request["steps"]![0]!["expectedResult"] = "  "; break;
            case "bad-priority": request["priority"] = "Unknown"; break;
            case "numeric-priority": request["priority"] = 999; break;
            case "archived": request["status"] = "Archived"; break;
            case "complete": request["status"] = "Complete"; break;
        }
        var response = await client.PostAsJsonAsync("/api/testcases", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.TestCases.CountAsync());
        Assert.Equal(0, await db.TestSteps.CountAsync());
    }

    [Fact]
    public async Task MissingParentsAndCases_Return404()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/modules", new { projectId = 999, name = "Missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/testcases", ValidRequest(999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/testcases/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/modules?projectId=999")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Project_RejectsEmptyNames(string name)
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/projects", new { name })).StatusCode);
    }

    [Fact]
    public async Task Complete_RequiresReadyAndSavedRun_ThenLocksCaseExceptDeletion()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        var module = await CreateModuleAsync(client);
        var request = ValidRequest(module.Id);
        request["status"] = "Draft";
        var created = await client.PostAsJsonAsync("/api/testcases", request);
        var testCase = (await created.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var caseUrl = $"/api/testcases/{testCase.Id}";
        var runsUrl = $"{caseUrl}/runs";

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"{caseUrl}/status", new { status = "Complete" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"{caseUrl}/status", new { status = "Ready" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"{caseUrl}/status", new { status = "Complete" })).StatusCode);

        var savedRun = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new object[]
            {
                new { stepId = testCase.Steps[0].Id, outcome = "Passed" },
                new { stepId = testCase.Steps[1].Id, outcome = "Failed", actualResult = "Wrong page",
                    canReplicate = true, onlyUserAffected = false }
            }
        });
        Assert.Equal(HttpStatusCode.Created, savedRun.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"{caseUrl}/status", new { status = "Complete" })).StatusCode);
        Assert.Equal("Complete", (await client.GetFromJsonAsync<TestCaseResponse>(caseUrl))!.Status);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"{caseUrl}/status", new { status = "Draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{caseUrl}/steps", new { action = "Another action", expectedResult = "Result" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{caseUrl}/steps/{testCase.Steps[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(runsUrl, new
            {
                steps = testCase.Steps.Select(step => new { stepId = step.Id, outcome = "Passed" }).ToArray()
            })).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(caseUrl)).StatusCode);
    }

    [Fact]
    public async Task AddStep_AppendsToCase_WithoutChangingSavedRuns()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        var module = await CreateModuleAsync(client);
        var created = await client.PostAsJsonAsync("/api/testcases", ValidRequest(module.Id));
        var testCase = (await created.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var runsUrl = $"/api/testcases/{testCase.Id}/runs";
        var run = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = testCase.Steps.Select(step => new { stepId = step.Id, outcome = "Passed" }).ToArray()
        });
        Assert.Equal(HttpStatusCode.Created, run.StatusCode);

        var invalid = await client.PostAsJsonAsync($"/api/testcases/{testCase.Id}/steps",
            new { action = "  ", expectedResult = "A result" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var added = await client.PostAsJsonAsync($"/api/testcases/{testCase.Id}/steps",
            new { action = "  Check profile  ", expectedResult = " Profile appears " });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var updated = (await added.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        Assert.Equal(3, updated.Steps.Count);
        Assert.Equal(3, updated.Steps[2].Position);
        Assert.Equal("Check profile", updated.Steps[2].Action);
        Assert.Equal("Profile appears", updated.Steps[2].ExpectedResult);
        Assert.True(updated.UpdatedAt >= testCase.UpdatedAt);

        var history = (await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl))!;
        Assert.Equal(2, Assert.Single(history).Steps.Count);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(runsUrl, new
            {
                steps = testCase.Steps.Select(step => new { stepId = step.Id, outcome = "Passed" }).ToArray()
            })).StatusCode);
    }

    [Fact]
    public async Task ManualRun_RequiresEveryStep_AndPreservesHistoryWhenDefinitionChanges()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        var module = await CreateModuleAsync(client);
        var request = ValidRequest(module.Id);
        request["status"] = "Draft";
        var createdResponse = await client.PostAsJsonAsync("/api/testcases", request);
        var testCase = (await createdResponse.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var runsUrl = $"/api/testcases/{testCase.Id}/runs";

        var incomplete = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new[] { new { stepId = testCase.Steps[0].Id, outcome = "Passed" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);

        var missingFailureDetails = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new object[]
            {
                new { stepId = testCase.Steps[0].Id, outcome = "Passed" },
                new { stepId = testCase.Steps[1].Id, outcome = "Failed" }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingFailureDetails.StatusCode);

        var savedResponse = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new object[]
            {
                new { stepId = testCase.Steps[0].Id, outcome = "Passed" },
                new { stepId = testCase.Steps[1].Id, outcome = "Failed",
                    actualResult = "An error page appeared", canReplicate = true, onlyUserAffected = false }
            }
        });
        Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ManualRunResponse>())!;
        Assert.Equal("Failed", saved.Result);
        Assert.Equal(2, saved.Steps.Count);
        Assert.Equal("An error page appeared", saved.Steps[1].ActualResult);
        Assert.True(saved.Steps[1].CanReplicate);
        Assert.False(saved.Steps[1].OnlyUserAffected);

        var remove = await client.DeleteAsync($"/api/testcases/{testCase.Id}/steps/{testCase.Steps[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var changedCase = await client.GetFromJsonAsync<TestCaseResponse>($"/api/testcases/{testCase.Id}");
        Assert.Single(changedCase!.Steps);
        Assert.Equal(1, changedCase.Steps[0].Position);

        var history = await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl);
        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal(2, history[0].Steps.Count);
        Assert.Equal("Open login", history[0].Steps[0].Action);
        Assert.Equal("An error page appeared", history[0].Steps[1].ActualResult);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.DeleteAsync($"/api/testcases/{testCase.Id}/steps/{changedCase.Steps[0].Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/testcases/{testCase.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/testcases/{testCase.Id}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.ManualRuns.CountAsync());
        Assert.Equal(0, await db.ManualStepResults.CountAsync());
        Assert.Equal(0, await db.TestSteps.CountAsync());
    }
}

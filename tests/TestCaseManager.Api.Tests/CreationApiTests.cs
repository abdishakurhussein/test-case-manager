using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Data;
using Microsoft.AspNetCore.Identity;
using TestCaseManager.Api.Models;


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

        public async Task<HttpClient> OpenAsUserAsync()
        {
            // Create the client and migrate its isolated test database.
            var client = await OpenAsync();

            // Add a user to that test database.
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var result = await users.CreateAsync(
                    new AppUser { UserName = "project-test-user", MustChangePassword = false },
                    "TestPassword123!");

                Assert.True(result.Succeeded);
            }

            // Get the CSRF token required for the login POST.
            using var csrf = await client.GetAsync("/api/auth/csrf");
            Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);

            var csrfCookie = csrf.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

            var token = Uri.UnescapeDataString(
                csrfCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

            // Sign in; the client retains the authentication cookie.
            using var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { userName = "project-test-user", password = "TestPassword123!" });

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            // Signing in changes the user identity, so get a fresh CSRF token
            // for subsequent POST requests such as creating a project.
            using var signedInCsrf = await client.GetAsync("/api/auth/csrf");
            Assert.Equal(HttpStatusCode.NoContent, signedInCsrf.StatusCode);

            var signedInCookie = signedInCsrf.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

            client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
            client.DefaultRequestHeaders.Add(
                "X-XSRF-TOKEN",
                Uri.UnescapeDataString(
                    signedInCookie.Split(';')[0]["XSRF-TOKEN=".Length..]));

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
        using var client = await factory.OpenAsUserAsync();
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

    [Fact]
    public async Task SearchCases_FiltersSortsAndPagesWithoutReturningEveryCase()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
        var module = await CreateModuleAsync(client);
        foreach (var (title, status, priority) in new[]
        {
            ("Alpha login", "Draft", "Minor"),
            ("Beta login", "Ready", "Major"),
            ("Gamma checkout", "Ready", "Critical")
        })
        {
            var request = ValidRequest(module.Id);
            request["title"] = title;
            request["status"] = status;
            request["priority"] = priority;
            Assert.Equal(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/testcases", request)).StatusCode);
        }

        var page = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            $"/api/testcases/search?projectId={module.ProjectId}&q=LOGIN&sort=title-asc&page=1&pageSize=1");
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("Alpha login", page.Items[0].Title);

        var second = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            $"/api/testcases/search?moduleId={module.Id}&status=Ready&sort=title-asc&page=2&pageSize=1");
        Assert.NotNull(second);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal("Gamma checkout", Assert.Single(second.Items).Title);

        var critical = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            "/api/testcases/search?priority=Critical");
        Assert.Equal("Gamma checkout", Assert.Single(critical!.Items).Title);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/testcases/search?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task ProjectOverview_AndCaseRows_SeparateLifecycleFromLatestRun()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
        var module = await CreateModuleAsync(client);
        var caseIds = new List<TestCaseResponse>();
        foreach (var (title, status) in new[] { ("Not started", "Draft"), ("Flaky", "Ready"),
            ("Done", "Ready"), ("Stable", "Ready") })
        {
            var request = ValidRequest(module.Id);
            request["title"] = title;
            request["status"] = status;
            var response = await client.PostAsJsonAsync("/api/testcases", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            caseIds.Add((await response.Content.ReadFromJsonAsync<TestCaseResponse>())!);
        }

        async Task SaveRun(TestCaseResponse item, bool failed)
        {
            var response = await client.PostAsJsonAsync($"/api/testcases/{item.Id}/runs", new
            {
                steps = new object[]
                {
                    new { stepId = item.Steps[0].Id, outcome = "Passed" },
                    new { stepId = item.Steps[1].Id, outcome = failed ? "Failed" : "Passed",
                        actualResult = failed ? "Error" : null, canReplicate = failed ? (bool?)true : null,
                        onlyUserAffected = failed ? (bool?)false : null }
                }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        await SaveRun(caseIds[1], false);
        await SaveRun(caseIds[1], true);
        await SaveRun(caseIds[2], true);
        await SaveRun(caseIds[3], false);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"/api/testcases/{caseIds[2].Id}/status", new { status = "Complete" })).StatusCode);

        var overview = await client.GetFromJsonAsync<ProjectOverviewResponse>($"/api/projects/{module.ProjectId}/overview");
        Assert.NotNull(overview);
        Assert.Equal(4, overview.TotalCases);
        Assert.Equal(1, overview.Draft);
        Assert.Equal(2, overview.Ready);
        Assert.Equal(1, overview.Complete);
        Assert.Equal(1, overview.LatestPassed);
        Assert.Equal(2, overview.LatestFailed);
        Assert.Equal(1, overview.NeverRun);

        var search = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            $"/api/testcases/search?projectId={module.ProjectId}");
        Assert.NotNull(search);
        var flaky = Assert.Single(search.Items, item => item.Title == "Flaky");
        Assert.Equal("Ready", flaky.Status);
        Assert.Equal(2, flaky.RunCount);
        Assert.Equal("Failed", flaky.LatestRunResult);
        Assert.Equal(DateTimeKind.Utc, flaky.LatestRunAt!.Value.Kind);
        var done = Assert.Single(search.Items, item => item.Title == "Done");
        Assert.Equal("Complete", done.Status);
        Assert.Equal("Failed", done.LatestRunResult);
        var notStarted = Assert.Single(search.Items, item => item.Title == "Not started");
        Assert.Equal(0, notStarted.RunCount);
        Assert.Null(notStarted.LatestRunResult);
        Assert.Null(notStarted.LatestRunAt);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/projects/999999/overview")).StatusCode);
    }

    [Fact]
    public async Task Archive_ExportsFullHistory_AndRestoresPreviousStatus()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
        var module = await CreateModuleAsync(client);
        var request = ValidRequest(module.Id);
        request["title"] = "=SUM(1,1)";
        var created = (await (await client.PostAsJsonAsync("/api/testcases", request))
            .Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var caseUrl = $"/api/testcases/{created.Id}";
        var runResponse = await client.PostAsJsonAsync(caseUrl + "/runs", new
        {
            steps = new object[]
            {
                new { stepId = created.Steps[0].Id, outcome = "Passed" },
                new { stepId = created.Steps[1].Id, outcome = "Failed", actualResult = "Incorrect result",
                    canReplicateUnknown = true, onlyUserAffectedUnknown = true }
            }
        });
        Assert.Equal(HttpStatusCode.Created, runResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync(caseUrl + "/status", new { status = "Complete" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(caseUrl + "/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(caseUrl + "/archive", null)).StatusCode);

        var active = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            $"/api/testcases/search?projectId={module.ProjectId}");
        Assert.Equal(0, active!.TotalCount);
        var archived = await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            $"/api/testcases/search?projectId={module.ProjectId}&status=Archived");
        var archivedCase = Assert.Single(archived!.Items);
        Assert.Equal("Complete", archivedCase.StatusBeforeArchive);
        Assert.NotNull(archivedCase.ArchivedAt);
        var project = await client.GetFromJsonAsync<ProjectResponse>($"/api/projects/{module.ProjectId}");
        Assert.Equal(0, project!.TestCaseCount);
        Assert.Equal(1, project.ArchivedCaseCount);
        var moduleNow = await client.GetFromJsonAsync<ModuleResponse>($"/api/modules/{module.Id}");
        Assert.Equal(0, moduleNow!.TestCaseCount);
        Assert.Equal(1, moduleNow.ArchivedCaseCount);
        var overview = await client.GetFromJsonAsync<ProjectOverviewResponse>($"/api/projects/{module.ProjectId}/overview");
        Assert.Equal(0, overview!.TotalCases);
        Assert.Equal(1, overview.Archived);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(caseUrl + "/runs", new
            {
                steps = new object[]
                {
                    new { stepId = created.Steps[0].Id, outcome = "Passed" },
                    new { stepId = created.Steps[1].Id, outcome = "Passed" }
                }
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync(caseUrl + "/status", new { status = "Draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(caseUrl + "/steps", new
            { action = "Unexpected edit", expectedResult = "Blocked" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.DeleteAsync(caseUrl + $"/steps/{created.Steps[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/archive/export?format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/export?projectId=999999")).StatusCode);

        var json = await client.GetAsync($"/api/archive/export?format=json&projectId={module.ProjectId}");
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Equal("application/json", json.Content.Headers.ContentType?.MediaType);
        using (var payload = JsonDocument.Parse(await json.Content.ReadAsStringAsync()))
        {
            var exported = Assert.Single(payload.RootElement.GetProperty("cases").EnumerateArray());
            Assert.Equal("=SUM(1,1)", exported.GetProperty("title").GetString());
            Assert.Equal(2, exported.GetProperty("steps").GetArrayLength());
            var historicalRun = Assert.Single(exported.GetProperty("runs").EnumerateArray());
            Assert.Equal("Failed", historicalRun.GetProperty("result").GetString());
            Assert.Equal("Incorrect result", historicalRun.GetProperty("steps")[1]
                .GetProperty("actualResult").GetString());
        }
        var csv = await client.GetAsync($"/api/archive/export?format=csv&projectId={module.ProjectId}");
        Assert.Equal("application/zip", csv.Content.Headers.ContentType?.MediaType);
        using (var stream = new MemoryStream(await csv.Content.ReadAsByteArrayAsync()))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            Assert.Equal(4, zip.Entries.Count);
            using var reader = new StreamReader(zip.GetEntry("cases.csv")!.Open());
            Assert.Contains("'=SUM(1,1)", await reader.ReadToEndAsync());
            Assert.NotNull(zip.GetEntry("case_steps.csv"));
            Assert.NotNull(zip.GetEntry("runs.csv"));
            Assert.NotNull(zip.GetEntry("run_steps.csv"));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(caseUrl + "/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(caseUrl + "/restore", null)).StatusCode);
        var restored = await client.GetFromJsonAsync<TestCaseResponse>(caseUrl);
        Assert.Equal("Complete", restored!.Status);
        Assert.Null(restored.ArchivedAt);
        Assert.Single((await client.GetFromJsonAsync<List<ManualRunResponse>>(caseUrl + "/runs"))!);
        Assert.Equal(0, (await client.GetFromJsonAsync<PagedResult<TestCaseSummary>>(
            "/api/testcases/search?status=Archived"))!.TotalCount);
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
        using var client = await factory.OpenAsUserAsync();
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
        using var client = await factory.OpenAsUserAsync();
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
        using var client = await factory.OpenAsUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/projects", new { name })).StatusCode);
    }

    [Fact]
    public async Task Complete_RequiresReadyAndSavedRun_ThenLocksCaseExceptDeletion()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
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
            (await client.PutAsJsonAsync(caseUrl, new
            {
                title = "Changed", priority = "Major", expectedUpdatedAt = testCase.UpdatedAt,
                steps = testCase.Steps.Select(step => new { id = step.Id, action = step.Action,
                    expectedResult = step.ExpectedResult }).ToArray()
            })).StatusCode);
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
        using var client = await factory.OpenAsUserAsync();
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
    public async Task UpdateCase_EditsAndReordersSteps_WithoutChangingRunHistory()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
        var module = await CreateModuleAsync(client);
        var testCase = (await (await client.PostAsJsonAsync("/api/testcases", ValidRequest(module.Id)))
            .Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var caseUrl = $"/api/testcases/{testCase.Id}";
        var runsUrl = $"{caseUrl}/runs";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(runsUrl, new
        {
            steps = testCase.Steps.Select(step => new { stepId = step.Id, outcome = "Passed" }).ToArray()
        })).StatusCode);

        var update = await client.PutAsJsonAsync(caseUrl, new
        {
            title = "  Updated login  ", description = " New description ",
            preconditions = " Open site ", priority = "Critical",
            expectedUpdatedAt = testCase.UpdatedAt,
            steps = new object[]
            {
                new { id = (int?)testCase.Steps[1].Id, action = "Revised submit", expectedResult = "Dashboard opens" },
                new { id = (int?)testCase.Steps[0].Id, action = "Open login", expectedResult = "Form appears" },
                new { id = (int?)null, action = "Check profile", expectedResult = "Profile opens" }
            }
        });
        Assert.True(update.StatusCode == HttpStatusCode.OK,
            await update.Content.ReadAsStringAsync());
        var edited = (await update.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        Assert.Equal("Updated login", edited.Title);
        Assert.Equal("New description", edited.Description);
        Assert.Equal("Critical", edited.Priority);
        Assert.Equal(new[] { "Revised submit", "Open login", "Check profile" },
            edited.Steps.Select(step => step.Action));
        Assert.Equal(new[] { 1, 2, 3 }, edited.Steps.Select(step => step.Position));
        Assert.Equal(testCase.Steps[1].Id, edited.Steps[0].Id);
        Assert.Equal(testCase.Steps[0].Id, edited.Steps[1].Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync(caseUrl, new
            {
                title = "Stale", priority = "Major", expectedUpdatedAt = testCase.UpdatedAt,
                steps = new[] { new { id = edited.Steps[0].Id, action = "Again", expectedResult = "Again" } }
            })).StatusCode);

        var remove = await client.PutAsJsonAsync(caseUrl, new
        {
            title = edited.Title, description = edited.Description, preconditions = edited.Preconditions,
            priority = edited.Priority, expectedUpdatedAt = edited.UpdatedAt,
            steps = new object[]
            {
                new { id = (int?)edited.Steps[0].Id, action = edited.Steps[0].Action,
                    expectedResult = edited.Steps[0].ExpectedResult },
                new { id = (int?)edited.Steps[2].Id, action = edited.Steps[2].Action,
                    expectedResult = edited.Steps[2].ExpectedResult }
            }
        });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(2, (await remove.Content.ReadFromJsonAsync<TestCaseResponse>())!.Steps.Count);
        var history = (await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl))!;
        Assert.Equal(new[] { "Open login", "Submit credentials" },
            Assert.Single(history).Steps.Select(step => step.Action));
    }

    [Fact]
    public async Task ManualRun_RequiresEveryStep_AndPreservesHistoryWhenDefinitionChanges()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();
        var module = await CreateModuleAsync(client);
        var request = ValidRequest(module.Id);
        request["status"] = "Draft";
        var createdResponse = await client.PostAsJsonAsync("/api/testcases", request);
        var testCase = (await createdResponse.Content.ReadFromJsonAsync<TestCaseResponse>())!;
        var runsUrl = $"/api/testcases/{testCase.Id}/runs";

        var draftRun = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = testCase.Steps.Select(step => new { stepId = step.Id, outcome = "Passed" }).ToArray()
        });
        Assert.Equal(HttpStatusCode.Conflict, draftRun.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl))!);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"/api/testcases/{testCase.Id}/status", new { status = "Ready" })).StatusCode);

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

        var unknownAnswers = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new object[]
            {
                new { stepId = testCase.Steps[0].Id, outcome = "Passed" },
                new { stepId = testCase.Steps[1].Id, outcome = "Failed", actualResult = "Intermittent error",
                    canReplicateUnknown = true, onlyUserAffectedUnknown = true }
            }
        });
        Assert.Equal(HttpStatusCode.Created, unknownAnswers.StatusCode);
        var unknownRun = (await unknownAnswers.Content.ReadFromJsonAsync<ManualRunResponse>())!;
        Assert.Null(unknownRun.Steps[1].CanReplicate);
        Assert.Null(unknownRun.Steps[1].OnlyUserAffected);

        var conflictingAnswer = await client.PostAsJsonAsync(runsUrl, new
        {
            steps = new object[]
            {
                new { stepId = testCase.Steps[0].Id, outcome = "Passed" },
                new { stepId = testCase.Steps[1].Id, outcome = "Failed", actualResult = "Error",
                    canReplicate = true, canReplicateUnknown = true, onlyUserAffected = false }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, conflictingAnswer.StatusCode);

        var remove = await client.DeleteAsync($"/api/testcases/{testCase.Id}/steps/{testCase.Steps[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var changedCase = await client.GetFromJsonAsync<TestCaseResponse>($"/api/testcases/{testCase.Id}");
        Assert.Single(changedCase!.Steps);
        Assert.Equal(1, changedCase.Steps[0].Position);

        var history = await client.GetFromJsonAsync<List<ManualRunResponse>>(runsUrl);
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.All(history, run => Assert.Equal(2, run.Steps.Count));
        Assert.All(history, run => Assert.Equal("Open login", run.Steps[0].Action));
        Assert.Contains(history, run => run.Steps[1].ActualResult == "An error page appeared");

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

    [Fact]
    public async Task Projects_AnonymousRequest_ReturnsUnauthorized()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsync();
        using var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidLogin_ReturnsUnauthorised()
    {
        // Start the API with an isolated test database and create an HTTP client.
        await using var factory = new ApiFactory();
        using var client = await factory.OpenAsUserAsync();

        // Request a CSRF token before sending a POST request.
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrfResponse.StatusCode);

        // Find the token in the response cookies.
        var xsrfCookie = csrfResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        // Extract the token value, leaving out the cookie name and settings.
        var token = Uri.UnescapeDataString(
            xsrfCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

        // Prepare a login request with credentials that should not exist.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName = "unknown-user",
                password = "wrong-password"
            })
        };

        // Send the CSRF token in the header expected by the API.
        request.Headers.Add("X-XSRF-TOKEN", token);

        // The API should reject the credentials, not the CSRF request.
        using var loginResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    

    [Fact]
    public async Task CreateProject_ValidRequest_ReturnsCreatedAndPersists()
    {
        
        // Each factory uses its own test database.
        await using var factory = new ApiFactory();

        // This client has signed in as the test user.
        using var client = await factory.OpenAsUserAsync();

        // Create a project through the real HTTP endpoint.
        using var createResponse = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                name = "Customer Portal",
                description = "Integration test project"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Check the project returned by the create request.
        var createdProject = await createResponse.Content
            .ReadFromJsonAsync<ProjectResponse>();

        Assert.NotNull(createdProject);
        Assert.Equal("Customer Portal", createdProject.Name);
        Assert.NotNull(createResponse.Headers.Location);

        // Make a separate GET request to prove it was persisted.
        var savedProject = await client.GetFromJsonAsync<ProjectResponse>(
            createResponse.Headers.Location);

        Assert.NotNull(savedProject);
        Assert.Equal(createdProject.Id, savedProject.Id);
        Assert.Equal(createdProject.Name, savedProject.Name);
        Assert.Equal(createdProject.Description, savedProject.Description);
    }

    // [Fact]
    // public async Task CreateProject_ValidRequest_ReturnsCreatedAndPersists()
    // {
    //     await using var factory = new ApiTestFactory();
    //     using var client = await factory.OpenAsUserAsync();

    //     using var create = await client.PostAsJsonAsync("/api/projects", new { name = "Customer Portal", description = "Unit Test project" });
    //     Assert.Equal(HttpStatusCode.Created, create.StatusCode);

    //     var project = await create.Content.ReadFromJsonAsync<ProjectResponse>();
    //     Assert.NotNull(project);
    //     Assert.Equal("Customer Portal", project.Name);
    //     Assert.NotNull(create.Headers.Location);

    //     var saved = await client.GetFromJsonAsync<ProjectResponse>(create.Headers.Location);

    //     Assert.Equal(project.Id, saved?.Id);
    // }
}

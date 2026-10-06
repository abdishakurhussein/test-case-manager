using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestCaseManager.Api.Contracts;
using TestCaseManager.Api.Controllers;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;
using Xunit;

namespace TestCaseManager.Api.Tests;

public class AttachmentApiTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "atcm-attachments-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Storage:Directory", _directory);
        }

        public async Task<HttpClient> OpenAsync(bool signIn)
        {
            var client = CreateClient();
            using (var scope = Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                if (signIn)
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                    var result = await users.CreateAsync(new AppUser { UserName = "feature-test", MustChangePassword = false }, "TestPassword123!");
                    Assert.True(result.Succeeded);
                }
            }
            if (!signIn) return client;
            using var csrf = await client.GetAsync("/api/auth/csrf");
            Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);
            var cookie = csrf.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]));
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "feature-test", password = "TestPassword123!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            // Signing in changes the principal; request a token for that principal.
            using var signedInCsrf = await client.GetAsync("/api/auth/csrf");
            Assert.Equal(HttpStatusCode.NoContent, signedInCsrf.StatusCode);
            var signedInCookie = signedInCsrf.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
            client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN",
                Uri.UnescapeDataString(signedInCookie.Split(';')[0]["XSRF-TOKEN=".Length..]));
            return client;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var full = Path.GetFullPath(_directory);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (full.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(full).StartsWith("atcm-attachments-", StringComparison.Ordinal) &&
                Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }

    [Fact]
    public async Task ExcelAndAttachmentEndpoints_RequireLogin()
    {
        await using var factory = new Factory();
        using var client = await factory.OpenAsync(signIn: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/archive/excel")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects/1/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/attachments/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/projects/1/workbooks/1")).StatusCode);
    }

    [Fact]
    public async Task SignedInUser_CanExportAndStoreWorkbookWithoutChangingResults()
    {
        await using var factory = new Factory();
        using var client = await factory.OpenAsync(signIn: true);
        using var create = await client.PostAsJsonAsync("/api/projects", new { name = "Workbook project" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var project = (await create.Content.ReadFromJsonAsync<TestCaseManager.Api.Contracts.ProjectResponse>())!;
        using var export = await client.GetAsync($"/api/projects/{project.Id}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));

        using var form = new MultipartFormDataContent();
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(body, "file", "offline-results.xlsx");
        form.Add(new StringContent("Offline review"), "caption");
        using var upload = await client.PostAsync($"/api/projects/{project.Id}/workbooks", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var stored = (await upload.Content.ReadFromJsonAsync<AttachmentsController.AttachmentSummary>())!;
        using var listing = await client.GetAsync($"/api/projects/{project.Id}/workbooks");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Contains("offline-results.xlsx", await listing.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.StoredAttachments.CountAsync());
        Assert.Equal(0, await db.ManualRuns.CountAsync());

        using var wrongProject = await client.DeleteAsync($"/api/projects/{project.Id + 1}/workbooks/{stored.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongProject.StatusCode);
        using var delete = await client.DeleteAsync($"/api/projects/{project.Id}/workbooks/{stored.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(0, await db.StoredAttachments.CountAsync());
        Assert.Equal(0, await db.ManualRuns.CountAsync());
    }

    [Fact]
    public async Task FailedRunStep_CanHoldImageEvidence()
    {
        await using var factory = new Factory();
        using var client = await factory.OpenAsync(signIn: true);
        var project = (await (await client.PostAsJsonAsync("/api/projects", new { name = "Evidence project" }))
            .Content.ReadFromJsonAsync<ProjectResponse>())!;
        var module = (await (await client.PostAsJsonAsync("/api/modules", new { projectId = project.Id, name = "Login" }))
            .Content.ReadFromJsonAsync<ModuleResponse>())!;
        var testCase = (await (await client.PostAsJsonAsync("/api/testcases", new
        {
            moduleId = module.Id, title = "Failure evidence", status = "Ready", priority = "Major",
            steps = new[] { new { action = "Open login", expectedResult = "Form appears" } }
        })).Content.ReadFromJsonAsync<TestCaseResponse>())!;
        using var runResponse = await client.PostAsJsonAsync($"/api/testcases/{testCase.Id}/runs", new
        {
            steps = new[] { new { stepId = testCase.Steps[0].Id, outcome = "Failed", actualResult = "Error page",
                canReplicate = true, onlyUserAffected = false } }
        });
        Assert.Equal(HttpStatusCode.Created, runResponse.StatusCode);
        var run = (await runResponse.Content.ReadFromJsonAsync<ManualRunResponse>())!;
        Assert.True(run.Steps[0].Id > 0);

        // A tiny PNG. The API checks its signature and stores it against this saved failed step.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l4sAAAAASUVORK5CYII=");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(png), "file", "failure.png");
        form.Add(new StringContent("Error on login"), "caption");
        using var upload = await client.PostAsync(
            $"/api/testcases/{testCase.Id}/runs/{run.Id}/steps/{run.Steps[0].Id}/evidence", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var evidence = await client.GetAsync($"/api/testcases/{testCase.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        Assert.Contains("Error on login", await evidence.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.StoredAttachments.CountAsync(item => item.Kind == "Evidence"));
    }
}

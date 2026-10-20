using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Tests.TestSupport;

/// <summary>Starts the real API with a separate, migrated SQLite database for each test.</summary>
internal sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private const string TestPassword = "TestPassword123!";
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "atcm-api-test-" + Guid.NewGuid().ToString("N"));

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

    public Task<HttpClient> OpenAsUserAsync() => OpenAsRoleAsync("project-test-user", isAdmin: false);

    public Task<HttpClient> OpenAsAdminAsync() => OpenAsRoleAsync("project-test-admin", isAdmin: true);

    private async Task<HttpClient> OpenAsRoleAsync(string userName, bool isAdmin)
    {
        var client = await OpenAsync();
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (isAdmin)
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                foreach (var roleName in new[] { "Admin", "User" })
                {
                    if (await roles.RoleExistsAsync(roleName)) continue;
                    var roleResult = await roles.CreateAsync(new IdentityRole(roleName));
                    Assert.True(roleResult.Succeeded);
                }
            }

            var user = new AppUser { UserName = userName, MustChangePassword = false };
            var createResult = await users.CreateAsync(user, TestPassword);
            Assert.True(createResult.Succeeded);
            if (isAdmin)
            {
                var assignResult = await users.AddToRoleAsync(user, "Admin");
                Assert.True(assignResult.Succeeded);
            }
        }

        await AuthTestHelpers.RefreshCsrfAsync(client);
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName, password = TestPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        // Signing in changes the principal, so later writes need a fresh token.
        await AuthTestHelpers.RefreshCsrfAsync(client);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var fullPath = Path.GetFullPath(_directory);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (fullPath.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fullPath).StartsWith("atcm-api-test-", StringComparison.Ordinal)
            && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}

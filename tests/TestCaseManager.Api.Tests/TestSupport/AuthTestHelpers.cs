using System.Net;
using System.Net.Http.Json;
using TestCaseManager.Api.Controllers;

namespace TestCaseManager.Api.Tests.TestSupport;

/// <summary>Common HTTP steps used by authentication integration tests.</summary>
internal static class AuthTestHelpers
{
    public static async Task<TemporaryCredentialResponse> CreateTemporaryUserAsync(
        HttpClient adminClient, string userName)
    {
        using var response = await adminClient.PostAsJsonAsync(
            "/api/admin/users", new { userName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TemporaryCredentialResponse>();
        Assert.NotNull(created);
        Assert.Equal(userName, created.UserName);
        Assert.False(string.IsNullOrWhiteSpace(created.TemporaryPassword));
        return created;
    }

    public static async Task<AuthUserResponse> LoginWithTemporaryPasswordAsync(
        HttpClient client, string userName, string temporaryPassword)
    {
        await RefreshCsrfAsync(client);
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName, password = temporaryPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var signedInUser = await login.Content.ReadFromJsonAsync<AuthUserResponse>();
        Assert.NotNull(signedInUser);
        Assert.Equal(userName, signedInUser.UserName);
        // CSRF tokens are bound to the current principal; refresh after login.
        await RefreshCsrfAsync(client);
        return signedInUser;
    }

    public static async Task RefreshCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }
}

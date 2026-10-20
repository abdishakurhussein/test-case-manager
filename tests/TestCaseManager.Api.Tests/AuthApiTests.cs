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
using TestCaseManager.Api.Controllers;
using TestCaseManager.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace TestCaseManager.Api.Tests;

public class AuthApiTests
{
    private const string TemporaryTestUserName = "temporary-test-user";

    //HELPERS

    private readonly ITestOutputHelper _output;
    public AuthApiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    

    
    
    //Login & Permissions Scenario 2 - Signed-out visitor requests the /api/auth/me endpoint and receives a 401 Unauthorized response.
    [Fact]
    public async Task GetMe_WithoutLogin_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new ApiTestFactory();
        using var client = await factory.OpenAsync();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    //Login & Permissions Scenario 5 - Signed-in non-admin user requests the create user endpoint and receives a 403 Forbidden response.
    [Fact]
    public async Task CreateUser_AsSignedInNonAdmin_ReturnsForbidden()
    {
        // This factory creates a separate temporary database.
        await using var factory = new ApiTestFactory();

        // This helper creates a non-admin account, logs it in,
        // and prepares the CSRF header for POST requests.
        using var client = await factory.OpenAsUserAsync();

        // Prove the request is authenticated before testing permissions.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // Attempt an admin-only operation. The server generates passwords,
        // so the request supplies only a username.
        using var response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { userName = "testuser" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Also prove the forbidden request did not create an account.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByNameAsync("testuser"));
    }

    //Login & Permissions Scenario 6 - Signed-in non-admin user requests the reset password endpoint and receives a 403 Forbidden response.
    [Fact]
    public async Task ResetPassword_AsSignedInNonAdmin_ReturnsForbidden()
    {

        await using var factory = new ApiTestFactory();

        using var client = await factory.OpenAsUserAsync();

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);



       string userId;
       string? originalHash;

       using(var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByNameAsync("project-test-user");
            Assert.NotNull(user);

            userId = user.Id;
            originalHash = user.PasswordHash;
        }

        using var response = await client.PostAsync($"/api/admin/users/{userId}/reset-password",content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(userId);
            Assert.NotNull(user);

            Assert.Equal(originalHash, user.PasswordHash);
            Assert.False(user.MustChangePassword);

        }


    }

    // Login & Permissions Scenario 9:
    // Admin creates a user; the account has the User role and must change its password.
    [Fact]
    public async Task CreateUser_AsAdmin_ReturnsOkAndPersistsUser() // CHANGED: accurate name
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.OpenAsAdminAsync();

        var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, "temporary-test-user");

        // Verify the account was actually saved in the isolated database.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedUser = await users.FindByNameAsync("temporary-test-user");

        Assert.NotNull(savedUser);
        Assert.Equal(credentials.UserId, savedUser.Id);
        Assert.True(await users.IsInRoleAsync(savedUser, "User"));
        Assert.True(savedUser.MustChangePassword);

        // CHANGED: verify the returned temporary password belongs to this account.
        Assert.True(
            await users.CheckPasswordAsync(savedUser, credentials.TemporaryPassword));
    }

    //Login & Permissions Scenario 10 - Temporary password user signs in
    [Fact]
    public async Task SignIn_AsUserWithTemporaryPassword_ReturnsOk()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.OpenAsAdminAsync();

        var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, "temporary-test-user");

        // Verify the account was actually saved in the isolated database.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedUser = await users.FindByNameAsync("temporary-test-user");

        _output.WriteLine($"10.1. Admin created temporary user {savedUser}");

        Assert.NotNull(savedUser);
        Assert.Equal(credentials.UserId, savedUser.Id);
        Assert.True(await users.IsInRoleAsync(savedUser, "User"));
        Assert.True(savedUser.MustChangePassword);

        // CHANGED: verify the returned temporary password belongs to this account.
        Assert.True(
            await users.CheckPasswordAsync(savedUser, credentials.TemporaryPassword));

        _output.WriteLine($"10.2. Verify the returned temporary password belongs to this account: {savedUser}");

        //Create a separate client from that same factory

        using var client2 = await factory.OpenAsync();
        // Get a CSRF token for the new, still-anonymous client.
        using var csrf = await client2.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);

        var csrfCookie = csrf.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        var token = Uri.UnescapeDataString(
            csrfCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

        client2.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

        // Send the login request as the newly created user—not as the admin.
        using var login = await client2.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = credentials.UserName,
                password = credentials.TemporaryPassword
            });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var signedInUser = await login.Content.ReadFromJsonAsync<AuthUserResponse>();
        Assert.NotNull(signedInUser);
        Assert.False(signedInUser.IsAdmin);
        Assert.True(signedInUser.MustChangePassword);

        _output.WriteLine($"10.3. New user {savedUser} signed in successfully. Scenario 10 PASSED.");
        

    }

    //Login & Permissions Scenario 11 - Password change validation fail
    [Fact]
    public async Task Password_ChangeValidation_ReturnsBadRequest()
    {
        //declare createdUser to be re-used
        string createdUser = "temporary-test-user";
        //Create new factory
        await using var factory = new ApiTestFactory();
        //Create client, opening with Admin permissions
        using var client = await factory.OpenAsAdminAsync();
        //Reusing CreateTemporaryUserAsync step
        var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, createdUser);
        //Verify account is actually saved in the isolated db
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedUser = await users.FindByNameAsync(createdUser);

        _output.WriteLine($"11.1. Admin created temporary user {savedUser}");

        //Assert here that the user is not null, the user exists, the role is user, and the user must change their password
        Assert.NotNull(savedUser);
        Assert.Equal(credentials.UserId, savedUser.Id);
        Assert.True(await users.IsInRoleAsync(savedUser, "User"));
        Assert.True(savedUser.MustChangePassword);

        //Verify the returned password belongs to this account:

        Assert.True(await users.CheckPasswordAsync(savedUser, credentials.TemporaryPassword));

        _output.WriteLine($"11.2. Verify the returned temporary password belongs to this account: {savedUser}");

        //Create a separate client from the same factory

        using var client2 = await factory.OpenAsync();

        // Reuse LoginRequest here
        var loginRequest = await AuthTestHelpers.LoginWithTemporaryPasswordAsync(client2, credentials.UserName,
        credentials.TemporaryPassword);

        //Get a CSRF token for client2
        using var csrf = await client2.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);

        var csrfCookie = csrf.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        var token = Uri.UnescapeDataString(
            csrfCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

        client2.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client2.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

        _output.WriteLine("11.3. 2nd client created, csrf, cookies and tokens validated.");

        //Change your password phase
        var changePasswordResponse = await client2.PostAsJsonAsync("/api/auth/change-password",
        new {
            currentPassword = "wrongrandompassword",
            newPassword = "AbdiTempTestUser1!"});

        //Assert that the password change failed
        Assert.Equal(HttpStatusCode.BadRequest, changePasswordResponse.StatusCode);

        var error = await changePasswordResponse.Content
    .ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Incorrect password.", error.GetProperty("errors")[0].GetString());
        
       _output.WriteLine("11.4. Change password phase sends bad request due to incorrect password entered, scenario 11 PASSED"); 
    }

    //Login & Permissions Scenario 12 - Password change success
    [Fact]
    public async Task Password_ChangeValidation_ReturnsOK()
    {
        //declare createdUser to be re-used
        string createdUser = TemporaryTestUserName;
        //Create new factory
        await using var factory = new ApiTestFactory();
        //Create client, opening with Admin permissions
        using var client = await factory.OpenAsAdminAsync();
        //Reusing CreateTemporaryUserAsync step
        var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, createdUser);
        //Verify account is actually saved in the isolated db
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedUser = await users.FindByNameAsync(createdUser);

        _output.WriteLine($"12.1. Admin created temporary user {savedUser}");

        //Assert here that the user is not null, the user exists, the role is user, and the user must change their password
        Assert.NotNull(savedUser);
        Assert.Equal(credentials.UserId, savedUser.Id);
        Assert.True(await users.IsInRoleAsync(savedUser, "User"));
        Assert.True(savedUser.MustChangePassword);

        //Verify the returned password belongs to this account:

        Assert.True(await users.CheckPasswordAsync(savedUser, credentials.TemporaryPassword));

        _output.WriteLine($"12.2. Verify the returned temporary password belongs to this account: {savedUser}");

        //Create a separate client from the same factory

        using var client2 = await factory.OpenAsync();

        // Reuse LoginRequest here
        var loginRequest = await AuthTestHelpers.LoginWithTemporaryPasswordAsync(client2, credentials.UserName,
        credentials.TemporaryPassword);

        //Get a CSRF token for client2
        using var csrf = await client2.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);

        var csrfCookie = csrf.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        var token = Uri.UnescapeDataString(
            csrfCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

        client2.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client2.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

        _output.WriteLine("12.3. Login request and CSRF refresh complete.");

        //Change your password phase
        var changePasswordResponse = await client2.PostAsJsonAsync("/api/auth/change-password",
        new {
            currentPassword = credentials.TemporaryPassword, 
            newPassword = "AbdiTempTestUser1!"});

        //Assert that the password change worked
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var changedUser = await changePasswordResponse.Content
    .ReadFromJsonAsync<AuthUserResponse>();

        Assert.NotNull(changedUser);
        Assert.False(changedUser.MustChangePassword);
        
        //Act - Confirmed that user has logged in
        var userResponse = await client2.GetAsync("/api/auth/me");
        //Assert - Logged in status OK
        Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);

        _output.WriteLine($"12.4. Changed password request returns OK, we assert that changedUser isn't null and no need to change password, and lastly, we authenticate to confirm that user remains signed in with the status OK. Scenario 12 PASSED");
    }

    

    //Login & Permissions Scenario 13 - Admin resets a user password
    //Login as admin (OpenAsAdminAsync)
    [Fact]
    public async Task Admin_ResetsUserPassword_ReturnsOK()
    {
    string createdUser = TemporaryTestUserName;
    await using var factory = new ApiTestFactory();
    using var client = await factory.OpenAsAdminAsync();

    // ACT - Admin Signed In
    var adminSignedIn = await client.GetAsync("/api/auth/me");
    
    //ASSERT - Confirm we are logged as an admin
    Assert.Equal(HttpStatusCode.OK, adminSignedIn.StatusCode);

    // reuse component CreateTemporaryUserAsync
    var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, createdUser);
    //Verify account is actually saved in the isolated db
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedUser = await users.FindByNameAsync(createdUser);


    _output.WriteLine($"13.1. Admin created temporary user {savedUser}");
    //Assert here that the user is not null, the user exists, the role is user, and the user must change their password
        Assert.NotNull(savedUser);
        Assert.Equal(credentials.UserId, savedUser.Id);
        Assert.True(await users.IsInRoleAsync(savedUser, "User"));
        Assert.True(savedUser.MustChangePassword);

    //Verify the returned password belongs to this account:

        Assert.True(await users.CheckPasswordAsync(savedUser, credentials.TemporaryPassword));

        _output.WriteLine($"12.2. Verify the returned temporary password belongs to this account: {savedUser}");   
    //Then navigate to manage user (api/admin/user), but we also need to confirm from the response body the json properties as we'll validate against that later in the test

    //ACT
    var adminResetsUserPass = await client.PostAsync($"/api/admin/users/{credentials.UserId}/reset-password",content: null);
    //ASSERT
    Assert.Equal(HttpStatusCode.OK, adminResetsUserPass.StatusCode);
    //DESERIALIZE
    var resettedUser = await adminResetsUserPass.Content.ReadFromJsonAsync<TemporaryCredentialResponse>();

    Assert.NotNull(resettedUser);
    //ID confirm that it's same
    Assert.Equal(credentials.UserId, resettedUser.UserId);
    //userName confirm that it's same
    Assert.Equal(credentials.UserName, resettedUser.UserName);
    //mustChangePassword is set to true, and isn't null or whitespace
    Assert.False(string.IsNullOrWhiteSpace(resettedUser.TemporaryPassword));
    //Temp passwords should now be different to before
    Assert.NotEqual(credentials.TemporaryPassword, resettedUser.TemporaryPassword);
    }

    


    

    //Then navigate to manage user (api/admin/user)
    //Resets user password (/api/admin/users/{id}/reset-password), make sure to grab the temporary password from the response body
    //lastly check (api/admin/users) and assert that user mustChangePassword is true

    //Login & Permissions Scenario 14 - Admin attempts to reset another admin
    [Fact]
    public async Task Admin_ResetsAdminPassword_ReturnsBadRequest()
        {
        string createdAdmin = "TemporaryTestAdmin";
        await using var factory = new ApiTestFactory();
        using var client = await factory.OpenAsAdminAsync();

        // ACT - Admin Sign in
        var adminSignedIn = await client.GetAsync("/api/auth/me");        

        //Assert - confirmed that we are logged in as admin
        Assert.Equal(HttpStatusCode.OK, adminSignedIn.StatusCode);

        //reuse component CreateTemporary
        var credentials = await AuthTestHelpers.CreateTemporaryUserAsync(client, createdAdmin);
        //Verify account is actually saved in the isolated db
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var savedAdmin = await users.FindByNameAsync(createdAdmin);

        Assert.NotNull(savedAdmin);

        var assignResult = await users.AddToRoleAsync(savedAdmin, "Admin");
        Assert.True(assignResult.Succeeded);
        Assert.True(await users.IsInRoleAsync(savedAdmin, "Admin"));


        

    //Login & Permissions Scenario 15 - Authenticated write without CSRF token

    //Login & Permissions Scenario 16 - Repeated wrong passwords
        }
    
}

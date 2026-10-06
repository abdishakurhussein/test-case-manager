using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Data;

public static class AdminBootstrap
{
    public static async Task CreateAsync(
        IServiceProvider services,
        string requestedUserName)
    {
        var userName = requestedUserName.Trim();

        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("An admin username is required.");

        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        // This command is only for the very first account.
        if (await users.Users.AnyAsync())
            throw new InvalidOperationException(
                "Users already exist. The first-admin bootstrap cannot run again.");

        foreach (var roleName in new[] { "Admin", "User" })
        {
            if (await roles.RoleExistsAsync(roleName))
                continue;

            var roleResult = await roles.CreateAsync(new IdentityRole(roleName));
            EnsureSucceeded(roleResult, $"creating the {roleName} role");
        }

        // 16 cryptographically random bytes, displayed as 32 hex characters.
        var temporaryPassword =
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var admin = new AppUser
        {
            UserName = userName,
            MustChangePassword = true
        };

        var createResult = await users.CreateAsync(admin, temporaryPassword);
        EnsureSucceeded(createResult, "creating the admin account");

        var assignRoleResult = await users.AddToRoleAsync(admin, "Admin");
        EnsureSucceeded(assignRoleResult, "assigning the Admin role");

        await transaction.CommitAsync();

        Console.WriteLine($"Admin username: {userName}");
        Console.WriteLine($"Temporary password (shown once): {temporaryPassword}");
        Console.WriteLine("Keep this password private. Change it at first login.");
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string action)
    {
        if (result.Succeeded)
            return;

        var errors = string.Join("; ",
            result.Errors.Select(error => error.Description));

        throw new InvalidOperationException(
            $"Failed while {action}: {errors}");
    }
}
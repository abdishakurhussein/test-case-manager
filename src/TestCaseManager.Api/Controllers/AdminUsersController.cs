using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public sealed class AdminUsersController(
    AppDbContext db,
    UserManager<AppUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserSummary>>> List()
    {
        var accounts = await users.Users
            .OrderBy(user => user.UserName)
            .ToListAsync();

        var result = new List<UserSummary>();

        foreach (var account in accounts)
        {
            result.Add(new UserSummary(
                account.Id,
                account.UserName ?? "",
                await users.IsInRoleAsync(account, "Admin"),
                account.MustChangePassword));
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TemporaryCredentialResponse>> Create(
        CreateUserRequest request)
    {
        var userName = request.UserName.Trim();
        if (userName.Length == 0)
            return BadRequest("A username is required.");

        var temporaryPassword = NewTemporaryPassword();

        await using var transaction = await db.Database.BeginTransactionAsync();

        var user = new AppUser
        {
            UserName = userName,
            MustChangePassword = true
        };

        var created = await users.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
            return BadRequest(Errors(created));

        var assigned = await users.AddToRoleAsync(user, "User");
        if (!assigned.Succeeded)
            return Problem("The user account could not be assigned its role.");

        await transaction.CommitAsync();

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new TemporaryCredentialResponse(
            user.Id, user.UserName ?? "", temporaryPassword));
    }

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<TemporaryCredentialResponse>> ResetPassword(
        string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        // For now, this screen resets ordinary users only.
        if (await users.IsInRoleAsync(user, "Admin"))
            return BadRequest("Admin passwords cannot be reset here.");

        var temporaryPassword = NewTemporaryPassword();

        await using var transaction = await db.Database.BeginTransactionAsync();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(
            user, token, temporaryPassword);

        if (!reset.Succeeded)
            return BadRequest(Errors(reset));

        user.MustChangePassword = true;

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
            return Problem("The password changed, but the account could not be updated.");

        // Invalidates the user's existing login cookie once Identity checks it.
        var stampUpdated = await users.UpdateSecurityStampAsync(user);
        if (!stampUpdated.Succeeded)
            return Problem("The account security stamp could not be updated.");

        await transaction.CommitAsync();

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new TemporaryCredentialResponse(
            user.Id, user.UserName ?? "", temporaryPassword));
    }

    private static string NewTemporaryPassword() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static object Errors(IdentityResult result) =>
        new { errors = result.Errors.Select(error => error.Description) };
}

public sealed class CreateUserRequest
{
    [Required]
    public string UserName { get; set; } = "";
}

public sealed record UserSummary(
    string Id,
    string UserName,
    bool IsAdmin,
    bool MustChangePassword);

public sealed record TemporaryCredentialResponse(
    string UserId,
    string UserName,
    string TemporaryPassword);
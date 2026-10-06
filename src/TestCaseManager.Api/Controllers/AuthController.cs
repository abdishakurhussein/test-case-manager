using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AutoValidateAntiforgeryToken]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        // Angular reads this cookie and sends it as the X-XSRF-TOKEN header.
        // This is NOT the authentication cookie.
        Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });

        Response.Headers["Cache-Control"] = "no-store";
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await ToResponse(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthUserResponse>> Login(
        LoginRequest request)
    {
        var user = await users.FindByNameAsync(request.UserName.Trim());

        if (user is null)
            return InvalidCredentials();

        var result = await signIn.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        // Keep the response identical for a wrong password and a locked account.
        if (!result.Succeeded)
            return InvalidCredentials();

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await ToResponse(user));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponse>> ChangePassword(
        ChangePasswordRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await users.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new
            {
                errors = result.Errors.Select(error => error.Description)
            });

        user.MustChangePassword = false;

        var updateResult = await users.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return Problem("The password changed, but the account could not be updated.");

        await signIn.RefreshSignInAsync(user);

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await ToResponse(user));
    }

    private async Task<AuthUserResponse> ToResponse(AppUser user) =>
        new(
            user.UserName ?? "",
            await users.IsInRoleAsync(user, "Admin"),
            user.MustChangePassword);

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails
        {
            Title = "Invalid username or password."
        });
}

public sealed class LoginRequest
{
    [Required]
    public string UserName { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required]
    public string NewPassword { get; set; } = "";
}

public sealed record AuthUserResponse(
    string UserName,
    bool IsAdmin,
    bool MustChangePassword);
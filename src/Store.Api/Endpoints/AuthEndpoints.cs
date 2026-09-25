using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Common;
using Store.Application.Identity;
using Store.Infrastructure.Identity;

namespace Store.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Cookie carrying the refresh token. HttpOnly so page script cannot read it, Secure so it
    /// never travels in clear text, SameSite=Strict so a cross-site request cannot silently mint
    /// a new access token, and path-scoped so it is only ever sent to the refresh endpoint.
    /// </summary>
    private const string RefreshCookie = "wps_rt";

    private const string RefreshCookiePath = "/api/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        // ---- Anonymous, rate-limited ------------------------------------------------------
        // These are the endpoints worth attacking, so they get the tight per-IP budget on top of
        // Identity's per-account lockout.

        group.MapPost("/register", Register)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Create a customer account and sign in.");

        group.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Sign in with email and password.");

        group.MapPost("/refresh", Refresh)
            .AllowAnonymous()
            .WithSummary("Exchange the refresh cookie for a new access token.");

        group.MapPost("/logout", Logout)
            .AllowAnonymous()
            .WithSummary("Revoke the current refresh token and clear the cookie.");

        group.MapPost("/forgot-password", ForgotPassword)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Begin a password reset.");

        group.MapPost("/reset-password", ResetPassword)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Complete a password reset.");

        // ---- Authenticated ----------------------------------------------------------------

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithSummary("The signed-in user, their roles and effective permissions.");

        group.MapPut("/me", UpdateProfile)
            .RequireAuthorization()
            .WithSummary("Update the signed-in user's profile.");

        group.MapPost("/change-password", ChangePassword)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .WithSummary("Change password. Revokes every other session.");

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        IAuthService auth,
        ICurrentUser currentUser,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, currentUser.IpAddress, currentUser.UserAgent, ct);

        if (result.Succeeded)
        {
            SetRefreshCookie(context, auth.LastIssuedRefreshToken);
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        IAuthService auth,
        ICurrentUser currentUser,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, currentUser.IpAddress, currentUser.UserAgent, ct);

        if (result.Succeeded)
        {
            SetRefreshCookie(context, auth.LastIssuedRefreshToken);
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> Refresh(
        IAuthService auth,
        ICurrentUser currentUser,
        HttpContext context,
        CancellationToken ct)
    {
        // Read from the cookie, never from the body: a token in the body would have to be
        // readable by script, which defeats the point of the HttpOnly cookie.
        var refreshToken = context.Request.Cookies[RefreshCookie];

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "No active session.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await auth.RefreshAsync(refreshToken, currentUser.IpAddress, currentUser.UserAgent, ct);

        if (result.Succeeded)
        {
            SetRefreshCookie(context, auth.LastIssuedRefreshToken);
        }
        else
        {
            // The session is dead — rotated away, expired, or revoked by reuse detection. Clearing
            // the cookie stops the client retrying a token that can never succeed again.
            ClearRefreshCookie(context);
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> Logout(IAuthService auth, HttpContext context, CancellationToken ct)
    {
        await auth.LogoutAsync(context.Request.Cookies[RefreshCookie], ct);
        ClearRefreshCookie(context);

        return Results.NoContent();
    }

    private static async Task<IResult> Me(IAuthService auth, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = GetUserId(user);

        return userId is null
            ? Results.Unauthorized()
            : (await auth.GetCurrentUserAsync(userId.Value, ct)).ToHttpResult();
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        IAuthService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = GetUserId(user);

        return userId is null
            ? Results.Unauthorized()
            : (await auth.UpdateProfileAsync(userId.Value, request, ct)).ToHttpResult();
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        IAuthService auth,
        ClaimsPrincipal user,
        HttpContext context,
        CancellationToken ct)
    {
        var userId = GetUserId(user);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await auth.ChangePasswordAsync(userId.Value, request, ct);

        if (result.Succeeded)
        {
            // Every session was revoked, including this one. Clearing the cookie makes the client
            // state match the server's rather than leaving a token that will fail on next use.
            ClearRefreshCookie(context);
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        IAuthService auth,
        IEmailSender email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var result = await auth.CreatePasswordResetTokenAsync(request.Email, ct);

        // A token only comes back for a real, active account. An empty value means the address is
        // unknown — and the response is identical either way, so this endpoint cannot be used to
        // discover which addresses are registered.
        if (result is { Succeeded: true, Value.Length: > 0 })
        {
            var baseUrl = configuration["Cors:AllowedOrigins:0"] ?? "https://waqasprovisionstore.com";
            var link = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(result.Value)}";

            try
            {
                await email.SendAsync(
                    request.Email,
                    "Reset your password",
                    $"""
                     <p>We received a request to reset your password.</p>
                     <p><a href="{link}">Choose a new password</a></p>
                     <p>This link expires shortly. If you did not request it, you can ignore this email.</p>
                     """,
                    ct);
            }
            catch (Exception ex)
            {
                // A mail failure must not change the response, or the timing difference becomes
                // the enumeration oracle the uniform response was meant to prevent.
                loggerFactory.CreateLogger("Auth.ForgotPassword")
                    .LogError(ex, "Failed to send a password reset email");
            }
        }

        return Results.Ok(new
        {
            message = "If that email address has an account, a reset link is on its way."
        });
    }

    private static async Task<IResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        IAuthService auth,
        CancellationToken ct) =>
        (await auth.ResetPasswordAsync(request, ct)).ToHttpResult(StatusCodes.Status200OK);

    // ---- Cookie helpers ---------------------------------------------------------------------

    private static void SetRefreshCookie(HttpContext context, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        context.Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,

            // Matches the refresh token's own lifetime, so the browser discards it at the same
            // moment the server stops honouring it.
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            IsEssential = true
        });
    }

    private static void ClearRefreshCookie(HttpContext context) =>
        context.Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            // Every attribute that scopes the cookie must match the one used to set it, or the
            // browser treats it as a different cookie and the deletion silently does nothing.
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath
        });

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

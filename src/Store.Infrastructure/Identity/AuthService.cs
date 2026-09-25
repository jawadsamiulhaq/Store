using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Application.Identity;
using Store.Domain.Customers;
using Store.Domain.Identity;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Identity;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? ip, string? userAgent, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default);
    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);
    Task<Result<CurrentUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<Result<string>> CreatePasswordResetTokenAsync(string email, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>The raw refresh token issued alongside the last successful auth call.</summary>
    string? LastIssuedRefreshToken { get; }
}

public sealed class AuthService(
    StoreDbContext db,
    UserManager<AppUser> userManager,
    ITokenService tokenService,
    IPermissionService permissionService,
    IDateTimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>
    /// Carries the raw refresh token out to the endpoint so it can be written as an HttpOnly
    /// cookie. It is deliberately not part of <see cref="AuthResponse"/>: anything in the JSON body
    /// is readable by page script, which is exactly what an HttpOnly cookie exists to prevent.
    /// </summary>
    public string? LastIssuedRefreshToken { get; private set; }

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Result<AuthResponse>.Conflict("An account with this email already exists.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            PhoneNumber = request.Phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow
        };

        var created = await userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return Result<AuthResponse>.Failure(
                string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, RoleNames.Customer);

        // Every registered shopper gets a Customer profile immediately, so cart merge, wishlist
        // and checkout never have to handle a missing profile as a special case.
        db.Customers.Add(new Customer
        {
            UserId = user.Id,
            AcceptsMarketing = request.AcceptsMarketing,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Registered new customer {UserId}", user.Id);

        return await IssueAsync(user, ip, userAgent, ct);
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);

        // One message for "no such user" and "wrong password" alike. Distinguishing them turns
        // the login form into an account-enumeration oracle.
        const string invalidCredentials = "Incorrect email or password.";

        if (user is null)
        {
            return Result<AuthResponse>.Failure(invalidCredentials, ErrorKind.Unauthorized);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Result<AuthResponse>.Failure(
                "This account is temporarily locked after too many failed sign-in attempts. Please try again later.",
                ErrorKind.Unauthorized);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Feeds Identity's lockout counter — this is what makes lockout work at all.
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Failed sign-in for {Email} from {Ip}", email, ip);
            return Result<AuthResponse>.Failure(invalidCredentials, ErrorKind.Unauthorized);
        }

        if (!user.IsActive)
        {
            return Result<AuthResponse>.Failure(
                "This account has been deactivated. Please contact us.", ErrorKind.Unauthorized);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        user.LastLoginAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await IssueAsync(user, ip, userAgent, ct);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(
        string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var rotated = await tokenService.RotateRefreshTokenAsync(refreshToken, ip, userAgent, ct);

        if (!rotated.Succeeded)
        {
            return Result<AuthResponse>.Failure(rotated.Error!, rotated.Kind);
        }

        var (user, newRefreshToken) = rotated.Value;
        LastIssuedRefreshToken = newRefreshToken;

        var (accessToken, expiresAt) = await tokenService.CreateAccessTokenAsync(user, ct);
        var dto = await BuildCurrentUserAsync(user, ct);

        return Result<AuthResponse>.Success(new AuthResponse(accessToken, expiresAt, dto));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await tokenService.RevokeRefreshTokenAsync(refreshToken, "Signed out", ct);
        }
    }

    public async Task<Result<CurrentUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Read-only: nothing here is mutated, so skip the change-tracking snapshot.
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null
            ? Result<CurrentUserDto>.NotFound("User not found.")
            : Result<CurrentUserDto>.Success(await BuildCurrentUserAsync(user, ct));
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.NotFound("User not found.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return Result.Failure(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // A password change must invalidate sessions elsewhere — otherwise changing a password
        // after a compromise does not actually evict the attacker.
        await tokenService.RevokeAllForUserAsync(userId, "Password changed", ct);

        logger.LogInformation("Password changed for user {UserId}; all sessions revoked", userId);
        return Result.Success();
    }

    public async Task<Result<string>> CreatePasswordResetTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());

        // Always reports success. Revealing whether an address is registered would make this
        // endpoint an account-enumeration oracle; the caller sends the email only when a token
        // actually comes back.
        if (user is null || !user.IsActive)
        {
            logger.LogInformation("Password reset requested for unknown or inactive address {Email}", email);
            return Result<string>.Success(string.Empty);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return Result<string>.Success(token);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());

        if (user is null)
        {
            return Result.Failure("This password reset link is invalid or has expired.");
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            return Result.Failure("This password reset link is invalid or has expired.");
        }

        await tokenService.RevokeAllForUserAsync(user.Id, "Password reset", ct);
        return Result.Success();
    }

    public async Task<Result<CurrentUserDto>> UpdateProfileAsync(
        Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Result<CurrentUserDto>.NotFound("User not found.");
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.Phone;
        user.AvatarUrl = request.AvatarUrl;
        user.UpdatedAt = clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
        {
            user.PreferredLanguage = request.PreferredLanguage;
        }

        await db.SaveChangesAsync(ct);
        return Result<CurrentUserDto>.Success(await BuildCurrentUserAsync(user, ct));
    }

    private async Task<Result<AuthResponse>> IssueAsync(
        AppUser user, string? ip, string? userAgent, CancellationToken ct)
    {
        var (accessToken, expiresAt) = await tokenService.CreateAccessTokenAsync(user, ct);
        LastIssuedRefreshToken = await tokenService.CreateRefreshTokenAsync(user, ip, userAgent, ct: ct);

        var dto = await BuildCurrentUserAsync(user, ct);
        return Result<AuthResponse>.Success(new AuthResponse(accessToken, expiresAt, dto));
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(AppUser user, CancellationToken ct)
    {
        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);

        var permissions = await permissionService.GetEffectivePermissionsAsync(user.Id, ct);

        var isSystem = roles.Contains(RoleNames.System, StringComparer.Ordinal);
        var isStaff = isSystem || roles.Contains(RoleNames.Admin, StringComparer.Ordinal);

        return new CurrentUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.PreferredLanguage,
            user.EmailConfirmed,
            roles,
            [.. permissions.OrderBy(p => p, StringComparer.Ordinal)],
            isSystem,
            isStaff);
    }
}

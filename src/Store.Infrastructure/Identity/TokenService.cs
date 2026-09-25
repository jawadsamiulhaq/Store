using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Store.Application.Common;
using Store.Domain.Identity;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "WaqasProvisionStore";
    public string Audience { get; set; } = "WaqasProvisionStore.Client";

    /// <summary>
    /// Signing key. Supplied by user-secrets in development and by the environment in production —
    /// never committed. Validated at startup so a missing key fails loudly at boot rather than
    /// silently issuing unverifiable tokens.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Deliberately short. Permissions are resolved server-side per request, but role membership
    /// and account status ride in the token, so a compromised or stale token must expire quickly.
    /// The refresh cookie keeps this invisible to the user.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}

public interface ITokenService
{
    /// <summary>Issues a signed access token for the user.</summary>
    Task<(string Token, DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(AppUser user, CancellationToken ct = default);

    /// <summary>
    /// Issues a refresh token. Returns the raw value, which is sent to the client as a cookie and
    /// never stored — only its hash is persisted.
    /// </summary>
    Task<string> CreateRefreshTokenAsync(AppUser user, string? ip, string? userAgent, Guid? familyId = null, CancellationToken ct = default);

    /// <summary>
    /// Validates a presented refresh token and rotates it.
    /// </summary>
    /// <remarks>
    /// Rotation with reuse detection: each refresh revokes the presented token and issues a
    /// successor in the same family. Presenting an <i>already revoked</i> token means the value
    /// leaked and is being replayed, so the entire family is revoked — logging the attacker and
    /// the legitimate user out together, which is the correct trade.
    /// </remarks>
    Task<Result<(AppUser User, string NewRefreshToken)>> RotateRefreshTokenAsync(
        string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);

    Task RevokeRefreshTokenAsync(string refreshToken, string reason, CancellationToken ct = default);

    /// <summary>Revokes every active token for a user — used on password change and deactivation.</summary>
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
}

public sealed class TokenService(
    StoreDbContext db,
    IOptions<JwtOptions> options,
    IDateTimeProvider clock,
    ILogger<TokenService> logger) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<(string Token, DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(AppUser user, CancellationToken ct = default)
    {
        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);

        var expiresAt = clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("name", user.FullName)
        };

        // Roles are claims; permissions deliberately are not. Permissions change far more often
        // and are far more numerous — embedding them would bloat every request header and leave
        // a revoked permission usable until the token expired.
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public async Task<string> CreateRefreshTokenAsync(
        AppUser user, string? ip, string? userAgent, Guid? familyId = null, CancellationToken ct = default)
    {
        // 256 bits from a cryptographic RNG. Not a GUID: GUIDs are identifiers, not secrets.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            FamilyId = familyId ?? Guid.CreateVersion7(),
            ExpiresAt = clock.UtcNow.AddDays(_options.RefreshTokenDays),
            CreatedAt = clock.UtcNow,
            CreatedByIp = ip,
            UserAgent = Truncate(userAgent, 500)
        });

        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<Result<(AppUser User, string NewRefreshToken)>> RotateRefreshTokenAsync(
        string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);

        // Tracked deliberately: this row is about to be revoked and linked to its successor, and
        // an untracked entity would make those writes silently vanish at SaveChanges.
        var existing = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
        {
            return Result<(AppUser, string)>.Failure("Invalid refresh token.", ErrorKind.Unauthorized);
        }

        // Reuse detection. A revoked token being presented again means the value leaked: either
        // an attacker is replaying it, or the legitimate client is replaying one the attacker
        // already spent. Either way the family is compromised, so all of it is revoked.
        if (existing.RevokedAt is not null)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId} from {Ip}. Revoking token family {FamilyId}.",
                existing.UserId, ip, existing.FamilyId);

            await db.RefreshTokens
                .Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, clock.UtcNow)
                    .SetProperty(t => t.RevokedReason, "Reuse detected"), ct);

            return Result<(AppUser, string)>.Failure(
                "This session is no longer valid. Please sign in again.", ErrorKind.Unauthorized);
        }

        if (existing.ExpiresAt <= clock.UtcNow)
        {
            return Result<(AppUser, string)>.Failure("Refresh token has expired.", ErrorKind.Unauthorized);
        }

        if (!existing.User.IsActive)
        {
            return Result<(AppUser, string)>.Failure("This account has been deactivated.", ErrorKind.Unauthorized);
        }

        // Issue the successor inside the same family, then revoke the presented token and link
        // the two so the chain stays walkable for reuse detection.
        var replacement = await CreateRefreshTokenAsync(existing.User, ip, userAgent, existing.FamilyId, ct);

        existing.RevokedAt = clock.UtcNow;
        existing.RevokedReason = "Rotated";
        existing.ReplacedByTokenHash = Hash(replacement);
        await db.SaveChangesAsync(ct);

        return Result<(AppUser, string)>.Success((existing.User, replacement));
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string reason, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);

        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, clock.UtcNow)
                .SetProperty(t => t.RevokedReason, reason), ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, clock.UtcNow)
                .SetProperty(t => t.RevokedReason, reason), ct);
    }

    /// <summary>
    /// SHA-256. Appropriate here — unlike a password, a refresh token is 256 bits of RNG output,
    /// so it is not brute-forceable and does not need a slow KDF. Hashing at rest means a database
    /// leak does not hand over usable sessions.
    /// </summary>
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

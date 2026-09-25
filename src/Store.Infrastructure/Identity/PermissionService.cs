using Microsoft.EntityFrameworkCore;
using Store.Application.Common;
using Store.Domain.Identity;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Identity;

/// <summary>Resolves what a user is actually allowed to do.</summary>
public interface IPermissionService
{
    /// <summary>
    /// The caller's effective permission set. Returns every defined permission for a System user.
    /// </summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);

    /// <summary>Drops the cached set for one user — call after any change to their roles or overrides.</summary>
    Task InvalidateUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Drops every cached set — call after a role's permissions change, which affects many users.</summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}

/// <summary>
/// The authoritative permission resolver.
/// </summary>
/// <remarks>
/// Resolution order, and why:
/// <list type="number">
///   <item>
///     <b>System role short-circuits to "everything".</b> The System role is defined as
///     unrestricted, so it is never expressed as a list of grants that could fall out of date as
///     new permissions are added.
///   </item>
///   <item>
///     <b>Union of role grants.</b> A user with several roles gets the union of what those roles allow.
///   </item>
///   <item>
///     <b>User-level overrides, with deny winning.</b> An explicit <c>IsGranted = false</c> removes
///     a capability even when a role grants it. Deny-wins is the safe default: revoking one
///     capability from one person must not require inventing a bespoke role for them.
///   </item>
/// </list>
/// The result is cached for ten minutes, because it is read on essentially every authorised
/// request and changes rarely. Every mutation path invalidates it explicitly, so the cache is a
/// latency optimisation and never a source of stale authorisation.
/// </remarks>
public sealed class PermissionService(
    StoreDbContext db,
    ICacheService cache) : IPermissionService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var permissions = await cache.GetOrCreateAsync(
            CacheKeys.UserPermissions(userId),
            async token => await ResolveAsync(userId, token),
            CacheLifetime,
            [CacheKeys.Tags.Permissions],
            ct);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        var permissions = await GetEffectivePermissionsAsync(userId, ct);
        return permissions.Contains(permission);
    }

    public Task InvalidateUserAsync(Guid userId, CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKeys.UserPermissions(userId), ct);

    public Task InvalidateAllAsync(CancellationToken ct = default) =>
        cache.RemoveByTagAsync(CacheKeys.Tags.Permissions, ct);

    /// <summary>
    /// Reads the permission set from the database.
    /// </summary>
    /// <remarks>
    /// Returns <c>string[]</c> rather than a set because the cache serialises the value, and an
    /// array round-trips through the distributed (L2) cache cleanly where a <c>HashSet</c> does not.
    /// </remarks>
    private async Task<string[]> ResolveAsync(Guid userId, CancellationToken ct)
    {
        // One query for role names. Also tells us whether this is a System user.
        // These are pure reads projecting to strings, so tracking would be wasted work.
        var roleNames = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToArrayAsync(ct);

        // System is unrestricted by definition, so it is resolved from the code catalogue rather
        // than from grant rows. A newly added permission therefore applies immediately, with no
        // migration and no risk of a System user silently lacking a new capability.
        if (roleNames.Contains(RoleNames.System, StringComparer.Ordinal))
        {
            return Permissions.All.ToArray();
        }

        var granted = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
            .Join(db.Permissions, id => id, p => p.Id, (_, p) => p.Code)
            .Distinct()
            .ToListAsync(ct);

        var overrides = await db.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == userId)
            .Select(up => new { up.Permission.Code, up.IsGranted })
            .ToListAsync(ct);

        var effective = granted.ToHashSet(StringComparer.Ordinal);

        foreach (var (code, isGranted) in overrides.Select(o => (o.Code, o.IsGranted)))
        {
            if (isGranted)
            {
                effective.Add(code);
            }
            else
            {
                // Deny wins over any role grant.
                effective.Remove(code);
            }
        }

        return [.. effective];
    }
}

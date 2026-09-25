using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Application.Identity;
using Store.Domain.Identity;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Identity;

public interface IUserAdminService
{
    Task<PagedResult<UserListItemDto>> ListAsync(UserQuery query, CancellationToken ct = default);
    Task<Result<UserDetailDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<UserDetailDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<Result<UserDetailDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> SetPermissionOverridesAsync(Guid id, SetUserPermissionsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Administration of user accounts, their roles, and their per-user permission overrides.
/// </summary>
/// <remarks>
/// Several guard rails here exist to stop an administrator escalating their own privileges or
/// locking the store out of its own admin area. They are enforced server-side because the frontend
/// cannot be trusted to hold a security boundary.
/// </remarks>
public sealed class UserAdminService(
    StoreDbContext db,
    UserManager<AppUser> userManager,
    IPermissionService permissionService,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<UserAdminService> logger) : IUserAdminService
{
    public async Task<PagedResult<UserListItemDto>> ListAsync(UserQuery query, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u =>
                u.Email!.Contains(term) ||
                u.FirstName.Contains(term) ||
                u.LastName.Contains(term) ||
                u.PhoneNumber!.Contains(term));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            users = users.Where(u => u.UserRoles.Any(ur => ur.Role.Name == query.Role));
        }

        users = query.SortBy?.ToLowerInvariant() switch
        {
            "email" => query.Descending ? users.OrderByDescending(u => u.Email) : users.OrderBy(u => u.Email),
            "name" => query.Descending ? users.OrderByDescending(u => u.FirstName) : users.OrderBy(u => u.FirstName),
            "lastlogin" => query.Descending ? users.OrderByDescending(u => u.LastLoginAt) : users.OrderBy(u => u.LastLoginAt),
            _ => query.Descending ? users.OrderBy(u => u.CreatedAt) : users.OrderByDescending(u => u.CreatedAt)
        };

        // Counted before paging, and counted on the filtered query rather than by materialising
        // rows — the pager needs a total, not the records.
        var total = await users.CountAsync(ct);

        // Projected in the database. Roles come back through a correlated subquery in the same
        // statement, so a page of 24 users is one round trip, not 1 + 24.
        var items = await users
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(u => new UserListItemDto(
                u.Id,
                u.Email!,
                u.FirstName + " " + u.LastName,
                u.PhoneNumber,
                u.AvatarUrl,
                u.IsActive,
                u.EmailConfirmed,
                u.UserRoles.Select(ur => ur.Role.Name!).ToList(),
                u.CreatedAt,
                u.LastLoginAt))
            .ToListAsync(ct);

        return new PagedResult<UserListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<Result<UserDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.AvatarUrl,
                u.IsActive,
                u.EmailConfirmed,
                u.PreferredLanguage,
                u.CreatedAt,
                u.LastLoginAt,
                Roles = u.UserRoles.Select(ur => ur.Role.Name!).ToList(),
                Overrides = u.UserPermissions
                    .Select(up => new UserPermissionOverrideDto(up.Permission.Code, up.IsGranted))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Result<UserDetailDto>.NotFound("User not found.");
        }

        var effective = await permissionService.GetEffectivePermissionsAsync(id, ct);

        return Result<UserDetailDto>.Success(new UserDetailDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.IsActive,
            user.EmailConfirmed,
            user.PreferredLanguage,
            user.Roles,
            [.. effective.OrderBy(p => p, StringComparer.Ordinal)],
            user.Overrides,
            user.CreatedAt,
            user.LastLoginAt));
    }

    public async Task<Result<UserDetailDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Result<UserDetailDto>.Conflict("An account with this email already exists.");
        }

        if (RejectSystemRoleGrant(request.Roles) is { } refusal)
        {
            return Result<UserDetailDto>.Forbidden(refusal);
        }

        // Creating an account is `users.create`; deciding it is a staff account is
        // `users.assign-roles`. Without this, the separation enforced on update could be walked
        // around by creating a new user with the roles instead of granting them to an existing one.
        if (request.Roles.Any(role => !string.Equals(role, RoleNames.Customer, StringComparison.Ordinal))
            && !await currentUser.HasPermissionAsync(Permissions.Users.AssignRoles, ct))
        {
            return Result<UserDetailDto>.Forbidden(
                "Creating a user with a role other than Customer needs the users.assign-roles permission.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.Phone,
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow
        };

        var created = await userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return Result<UserDetailDto>.Failure(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var roles = request.Roles.Count > 0 ? request.Roles : [RoleNames.Customer];
        await userManager.AddToRolesAsync(user, roles);

        logger.LogInformation("Created user {UserId} with roles {Roles}", user.Id, string.Join(", ", roles));

        return await GetAsync(user.Id, ct);
    }

    public async Task<Result<UserDetailDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return Result<UserDetailDto>.NotFound("User not found.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var isTargetSystem = currentRoles.Contains(RoleNames.System, StringComparer.Ordinal);

        // Only a System user may alter another System user. Without this, an Admin holding
        // users.update could deactivate the owner's account.
        if (isTargetSystem && !currentUser.IsSystem)
        {
            return Result<UserDetailDto>.Forbidden("Only a System user can modify another System user.");
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.Phone;
        user.UpdatedAt = clock.UtcNow;

        // Deactivating the last active System account would lock everyone out of the admin area
        // permanently, with no way back in through the UI.
        if (!request.IsActive && isTargetSystem && await IsLastActiveSystemUserAsync(id, ct))
        {
            return Result<UserDetailDto>.Conflict(
                "This is the last active System user. Deactivating it would lock everyone out of the admin area.");
        }

        user.IsActive = request.IsActive;

        if (request.Roles is not null)
        {
            if (RejectSystemRoleGrant(request.Roles) is { } refusal)
            {
                return Result<UserDetailDto>.Forbidden(refusal);
            }

            var toRemove = currentRoles.Except(request.Roles, StringComparer.Ordinal).ToList();
            var toAdd = request.Roles.Except(currentRoles, StringComparer.Ordinal).ToList();

            // `users.assign-roles` is a separate permission from `users.update` for a reason:
            // editing someone's phone number and deciding what they may do are different powers,
            // and roles are how every admin capability is granted. It is checked here rather than
            // on the endpoint because this one route both updates a profile and sets roles — so
            // the requirement has to depend on whether the roles actually changed, otherwise
            // someone with only `users.update` could not correct a typo in a name.
            if ((toRemove.Count > 0 || toAdd.Count > 0)
                && !await currentUser.HasPermissionAsync(Permissions.Users.AssignRoles, ct))
            {
                return Result<UserDetailDto>.Forbidden(
                    "Changing someone's roles needs the users.assign-roles permission.");
            }

            if (toRemove.Contains(RoleNames.System, StringComparer.Ordinal)
                && await IsLastActiveSystemUserAsync(id, ct))
            {
                return Result<UserDetailDto>.Conflict(
                    "This is the last System user. Removing the System role would leave the store unmanageable.");
            }

            if (toRemove.Count > 0) await userManager.RemoveFromRolesAsync(user, toRemove);
            if (toAdd.Count > 0) await userManager.AddToRolesAsync(user, toAdd);
        }

        await db.SaveChangesAsync(ct);

        // Role membership changed, so the cached permission set is stale.
        await permissionService.InvalidateUserAsync(id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return Result.NotFound("User not found.");
        }

        if (id == currentUser.UserId)
        {
            return Result.Conflict("You cannot delete your own account.");
        }

        var roles = await userManager.GetRolesAsync(user);

        if (roles.Contains(RoleNames.System, StringComparer.Ordinal) && !currentUser.IsSystem)
        {
            return Result.Forbidden("Only a System user can delete another System user.");
        }

        if (await IsLastActiveSystemUserAsync(id, ct))
        {
            return Result.Conflict("This is the last System user and cannot be deleted.");
        }

        // Deactivated, not removed. A customer's account is referenced by their order history, and
        // deleting the row would either cascade that away or leave orphaned records. Deactivation
        // revokes access while keeping the financial trail intact.
        user.IsActive = false;
        user.UpdatedAt = clock.UtcNow;

        await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, clock.UtcNow)
                .SetProperty(t => t.RevokedReason, "Account deactivated"), ct);

        await db.SaveChangesAsync(ct);
        await permissionService.InvalidateUserAsync(id, ct);

        logger.LogInformation("Deactivated user {UserId} and revoked their sessions", id);
        return Result.Success();
    }

    public async Task<Result> SetPermissionOverridesAsync(
        Guid id, SetUserPermissionsRequest request, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            return Result.NotFound("User not found.");
        }

        var valid = await db.Permissions
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, ct);

        var unknown = request.Overrides
            .Select(o => o.PermissionCode)
            .Where(code => !valid.ContainsKey(code))
            .ToList();

        if (unknown.Count > 0)
        {
            return Result.Failure($"Unknown permissions: {string.Join(", ", unknown)}");
        }

        // Replace wholesale rather than merge: the admin UI sends the complete override set, and
        // a partial merge would make removing an override impossible.
        await db.UserPermissions.Where(up => up.UserId == id).ExecuteDeleteAsync(ct);

        db.UserPermissions.AddRange(request.Overrides.Select(o => new UserPermission
        {
            UserId = id,
            PermissionId = valid[o.PermissionCode],
            IsGranted = o.IsGranted,
            GrantedAt = clock.UtcNow,
            GrantedBy = currentUser.UserId
        }));

        await db.SaveChangesAsync(ct);
        await permissionService.InvalidateUserAsync(id, ct);

        logger.LogInformation(
            "Set {Count} permission overrides on user {UserId} by {ActorId}",
            request.Overrides.Count, id, currentUser.UserId);

        return Result.Success();
    }

    /// <summary>
    /// Blocks anyone but a System user from handing out the System role — the one role that
    /// bypasses every permission check. An Admin with <c>users.assign-roles</c> must not be able
    /// to promote themselves to unrestricted access.
    /// </summary>
    private string? RejectSystemRoleGrant(IReadOnlyList<string> roles) =>
        roles.Contains(RoleNames.System, StringComparer.Ordinal) && !currentUser.IsSystem
            ? "Only a System user can grant the System role."
            : null;

    private async Task<bool> IsLastActiveSystemUserAsync(Guid excludingUserId, CancellationToken ct) =>
        !await db.UserRoles
            .Where(ur => ur.Role.Name == RoleNames.System && ur.UserId != excludingUserId)
            .Join(db.Users.Where(u => u.IsActive), ur => ur.UserId, u => u.Id, (_, u) => u.Id)
            .AnyAsync(ct);
}

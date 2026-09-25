using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Application.Identity;
using Store.Domain.Identity;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Identity;

public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<Result<RoleDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<RoleDto>> SetPermissionsAsync(Guid id, SetRolePermissionsRequest request, CancellationToken ct = default);

    /// <summary>The full permission catalogue, grouped by module for the admin matrix.</summary>
    Task<IReadOnlyList<PermissionModuleDto>> GetPermissionCatalogueAsync(CancellationToken ct = default);
}

public sealed class RoleAdminService(
    StoreDbContext db,
    RoleManager<AppRole> roleManager,
    IPermissionService permissionService,
    ICurrentUser currentUser,
    ILogger<RoleAdminService> logger) : IRoleAdminService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default) =>
        await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Id,
                r.Name!,
                r.Description,
                r.IsSystemRole,
                r.UserRoles.Count,
                r.RolePermissions.Select(rp => rp.Permission.Code).ToList()))
            .ToListAsync(ct);

    public async Task<Result<RoleDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RoleDto(
                r.Id,
                r.Name!,
                r.Description,
                r.IsSystemRole,
                r.UserRoles.Count,
                r.RolePermissions.Select(rp => rp.Permission.Code).ToList()))
            .FirstOrDefaultAsync(ct);

        return role is null ? Result<RoleDto>.NotFound("Role not found.") : Result<RoleDto>.Success(role);
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (RoleNames.All.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return Result<RoleDto>.Conflict($"'{name}' is a reserved role name.");
        }

        if (await roleManager.RoleExistsAsync(name))
        {
            return Result<RoleDto>.Conflict("A role with this name already exists.");
        }

        var role = new AppRole
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = request.Description,
            IsSystemRole = false
        };

        var created = await roleManager.CreateAsync(role);

        if (!created.Succeeded)
        {
            return Result<RoleDto>.Failure(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        if (request.Permissions.Count > 0)
        {
            var assign = await SetPermissionsAsync(role.Id, new SetRolePermissionsRequest(request.Permissions), ct);

            if (!assign.Succeeded)
            {
                return assign;
            }
        }

        logger.LogInformation("Created role {Role} with {Count} permissions", name, request.Permissions.Count);
        return await GetAsync(role.Id, ct);
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null)
        {
            return Result<RoleDto>.NotFound("Role not found.");
        }

        // System, Admin and Customer are referenced by name throughout authorisation and seeding.
        // Renaming one would silently break every check that depends on it.
        if (role.IsSystemRole && !string.Equals(role.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            return Result<RoleDto>.Forbidden("Built-in roles cannot be renamed.");
        }

        role.Name = request.Name.Trim();
        role.NormalizedName = role.Name.ToUpperInvariant();
        role.Description = request.Description;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null)
        {
            return Result.NotFound("Role not found.");
        }

        if (role.IsSystemRole)
        {
            return Result.Forbidden("Built-in roles cannot be deleted.");
        }

        var memberCount = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);

        if (memberCount > 0)
        {
            // Deleting would silently strip capabilities from real people. Making the caller move
            // them first keeps the change deliberate.
            return Result.Conflict(
                $"This role still has {memberCount} member(s). Reassign them before deleting it.");
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);

        await permissionService.InvalidateAllAsync(ct);

        logger.LogInformation("Deleted role {Role}", role.Name);
        return Result.Success();
    }

    public async Task<Result<RoleDto>> SetPermissionsAsync(
        Guid id, SetRolePermissionsRequest request, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null)
        {
            return Result<RoleDto>.NotFound("Role not found.");
        }

        // The System role bypasses permission evaluation entirely, so grant rows on it would be
        // decorative — and misleading to anyone reading the matrix.
        if (string.Equals(role.Name, RoleNames.System, StringComparison.Ordinal))
        {
            return Result<RoleDto>.Forbidden(
                "The System role has unrestricted access by definition and cannot be given explicit permissions.");
        }

        // Only a System user may widen what administrators can do. Otherwise an Admin holding
        // roles.assign-permissions could grant their own role every remaining capability.
        if (!currentUser.IsSystem)
        {
            return Result<RoleDto>.Forbidden("Only a System user can change a role's permissions.");
        }

        var valid = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var unknown = request.Permissions.Where(code => !valid.ContainsKey(code)).ToList();

        if (unknown.Count > 0)
        {
            return Result<RoleDto>.Failure($"Unknown permissions: {string.Join(", ", unknown)}");
        }

        // Replaced wholesale — the admin matrix posts the complete set, so a merge would make
        // revoking a permission impossible.
        await db.RolePermissions.Where(rp => rp.RoleId == id).ExecuteDeleteAsync(ct);

        db.RolePermissions.AddRange(request.Permissions.Distinct(StringComparer.Ordinal).Select(code =>
            new RolePermission
            {
                RoleId = id,
                PermissionId = valid[code],
                GrantedBy = currentUser.UserId
            }));

        await db.SaveChangesAsync(ct);

        // A role change affects every member, so the whole permission cache is dropped rather
        // than trying to enumerate the affected users.
        await permissionService.InvalidateAllAsync(ct);

        logger.LogInformation(
            "Set {Count} permissions on role {Role} by {ActorId}",
            request.Permissions.Count, role.Name, currentUser.UserId);

        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<PermissionModuleDto>> GetPermissionCatalogueAsync(CancellationToken ct = default)
    {
        var permissions = await db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.DisplayOrder).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Module, p.DisplayName, p.Description))
            .ToListAsync(ct);

        return permissions
            .GroupBy(p => p.Module)
            .Select(g => new PermissionModuleDto(g.Key, g.ToList()))
            .ToList();
    }
}

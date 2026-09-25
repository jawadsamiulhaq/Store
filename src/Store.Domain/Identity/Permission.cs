using Store.Domain.Common;

namespace Store.Domain.Identity;

/// <summary>
/// A single capability, addressed as <c>module.action</c> (e.g. <c>products.create</c>).
/// Rows are seeded from the <c>Permissions</c> constants class and are never user-created,
/// so the code and the database cannot drift apart.
/// </summary>
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Ordering within the module when rendering the permission matrix in admin.</summary>
    public int DisplayOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<UserPermission> UserPermissions { get; set; } = [];
}

/// <summary>Grants a permission to every member of a role.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? GrantedBy { get; set; }
}

/// <summary>
/// Per-user override of a role-derived permission.
/// <see cref="IsGranted"/> = <c>false</c> is an explicit <b>deny</b> and wins over any role grant,
/// which is what makes it possible to carve a capability away from one person without
/// inventing a new role for them.
/// </summary>
public class UserPermission
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public bool IsGranted { get; set; }

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? GrantedBy { get; set; }
}

/// <summary>
/// Refresh token, stored only as a hash. Tokens rotate on every use: the old token is revoked
/// and linked to its replacement, forming a family chain. Presenting an already-revoked token
/// means it leaked, so the entire family is revoked at once (reuse detection).
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>SHA-256 of the token. The raw value exists only in the client's cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Groups a rotation chain so a leak can revoke every descendant in one statement.</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}

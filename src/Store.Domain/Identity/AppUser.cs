using Microsoft.AspNetCore.Identity;
using Store.Domain.Customers;

namespace Store.Domain.Identity;

/// <summary>
/// Application user. Backs every role — System, Admin and Customer all live here.
/// Storefront-specific data hangs off <see cref="Customer"/> so staff accounts do not
/// carry empty commerce columns.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    /// <summary>Disabled accounts keep their history but cannot authenticate.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Preferred UI language. The catalogue is English-first with Chinese support planned.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public Customer? Customer { get; set; }
    public ICollection<AppUserRole> UserRoles { get; set; } = [];

    /// <summary>Per-user grant/deny overrides layered on top of role permissions.</summary>
    public ICollection<UserPermission> UserPermissions { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    /// <summary>
    /// System roles (System, Admin, Customer) are seeded and cannot be renamed or deleted,
    /// because authorization logic and seeding both depend on their names.
    /// </summary>
    public bool IsSystemRole { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AppUserRole> UserRoles { get; set; } = [];
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

/// <summary>Join entity, declared explicitly so both navigation sides are available for projection.</summary>
public class AppUserRole : IdentityUserRole<Guid>
{
    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
}

public class AppUserClaim : IdentityUserClaim<Guid>;
public class AppUserLogin : IdentityUserLogin<Guid>;
public class AppUserToken : IdentityUserToken<Guid>;
public class AppRoleClaim : IdentityRoleClaim<Guid>;

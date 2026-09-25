using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Store.Domain.Identity;

namespace Store.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds permissions, roles and the bootstrap System account.
/// </summary>
/// <remarks>
/// Idempotent by design: it reconciles the database against the <see cref="Permissions"/>
/// constants rather than inserting blindly, so it is safe to run on every startup and a newly
/// added permission constant reaches production without a hand-written migration.
/// </remarks>
public sealed class IdentitySeeder(
    StoreDbContext db,
    RoleManager<AppRole> roleManager,
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SeedAdminPermissionsAsync(ct);
        await SeedSystemUserAsync();
    }

    /// <summary>
    /// Reconciles the Permissions table with the code constants: inserts what is new, removes what
    /// no longer exists in code. Code is the source of truth.
    /// </summary>
    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var expected = Permissions.All.ToHashSet(StringComparer.Ordinal);

        var toAdd = expected
            .Where(code => !existing.ContainsKey(code))
            .Select(code =>
            {
                var parts = code.Split('.');
                var module = parts[0];
                var action = parts.Length > 1 ? parts[1] : code;

                return new Permission
                {
                    Code = code,
                    Module = module,
                    DisplayName = Humanise(action),
                    Description = $"Allows the holder to {Humanise(action).ToLowerInvariant()} within {Humanise(module).ToLowerInvariant()}.",
                    DisplayOrder = Array.IndexOf(ActionOrder, action) is var i and >= 0 ? i : 99
                };
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Permissions.AddRange(toAdd);
            logger.LogInformation("Seeding {Count} new permissions", toAdd.Count);
        }

        // A permission removed from code must not linger as a grantable capability.
        var stale = existing.Values.Where(p => !expected.Contains(p.Code)).ToList();
        if (stale.Count > 0)
        {
            db.Permissions.RemoveRange(stale);
            logger.LogWarning("Removing {Count} permissions no longer defined in code: {Codes}",
                stale.Count, string.Join(", ", stale.Select(p => p.Code)));
        }

        if (toAdd.Count > 0 || stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var descriptions = new Dictionary<string, string>
        {
            [RoleNames.System] = "Unrestricted access. Bypasses all permission checks and can manage administrators.",
            [RoleNames.Admin] = "Staff access. Holds only the permissions explicitly granted to it.",
            [RoleNames.Customer] = "Storefront access, scoped to the customer's own orders, cart, wishlist and reviews."
        };

        foreach (var roleName in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var result = await roleManager.CreateAsync(new AppRole
            {
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
                Description = descriptions[roleName],
                IsSystemRole = true
            });

            if (result.Succeeded)
            {
                logger.LogInformation("Created role {Role}", roleName);
            }
            else
            {
                logger.LogError("Failed to create role {Role}: {Errors}",
                    roleName, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Grants the Admin role a sensible starting set.
    /// </summary>
    /// <remarks>
    /// Runs only when Admin has no grants at all — a first-run bootstrap. Re-running must never
    /// re-add a permission an operator has deliberately revoked, so this deliberately does not
    /// reconcile the way <see cref="SeedPermissionsAsync"/> does.
    /// <para>
    /// The System role is intentionally given <b>no</b> rows: it bypasses permission evaluation
    /// entirely in the authorization handler, so granting it rows would be misleading.
    /// </para>
    /// </remarks>
    private async Task SeedAdminPermissionsAsync(CancellationToken ct)
    {
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Admin, ct);
        if (adminRole is null)
            return;

        if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id, ct))
            return;

        // Everything except the capabilities that let an administrator escalate themselves or
        // another account. Those stay with System until deliberately granted.
        string[] withheld =
        [
            Permissions.Users.Delete,
            Permissions.Users.ManagePermissions,
            Permissions.Roles.Create,
            Permissions.Roles.Delete,
            Permissions.Roles.AssignPermissions,
            Permissions.Customers.Delete,
            Permissions.Settings.Manage,
            Permissions.Audit.View
        ];

        var granted = await db.Permissions
            .Where(p => !withheld.Contains(p.Code))
            .Select(p => p.Id)
            .ToListAsync(ct);

        db.RolePermissions.AddRange(granted.Select(id => new RolePermission
        {
            RoleId = adminRole.Id,
            PermissionId = id
        }));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Granted {Count} default permissions to the Admin role", granted.Count);
    }

    /// <summary>
    /// Creates the bootstrap System account if no System user exists.
    /// </summary>
    /// <remarks>
    /// The password comes from configuration (<c>Seed:SystemUser:Password</c>), which in
    /// development is supplied by user-secrets and in production by the environment. If it is
    /// missing, seeding is skipped with a loud warning rather than falling back to a default
    /// credential — a well-known seeded password is how stores get taken over.
    /// </remarks>
    private async Task SeedSystemUserAsync()
    {
        var email = configuration["Seed:SystemUser:Email"];
        var password = configuration["Seed:SystemUser:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "System user not seeded: Seed:SystemUser:Email and Seed:SystemUser:Password are not configured. " +
                "Set them via user-secrets (development) or environment variables (production).");
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = configuration["Seed:SystemUser:FirstName"] ?? "System",
            LastName = configuration["Seed:SystemUser:LastName"] ?? "Administrator",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            logger.LogError("Failed to create the System user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, RoleNames.System);
        logger.LogInformation("Created the System user {Email}", email);
    }

    /// <summary>Orders actions consistently in the admin permission matrix.</summary>
    private static readonly string[] ActionOrder =
        ["view", "create", "update", "delete", "publish", "import", "export"];

    /// <summary>Turns <c>assign-roles</c> into <c>Assign roles</c> for display.</summary>
    private static string Humanise(string value)
    {
        var spaced = value.Replace('-', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}

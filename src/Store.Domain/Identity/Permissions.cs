using System.Reflection;

namespace Store.Domain.Identity;

/// <summary>
/// The canonical permission catalogue. These constants are the single source of truth: the
/// database table is seeded from them on startup, and endpoints reference them by constant rather
/// than by magic string, so a typo is a compile error instead of a silently open endpoint.
/// </summary>
public static class Permissions
{
    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Update = "users.update";
        public const string Delete = "users.delete";
        public const string AssignRoles = "users.assign-roles";
        public const string ManagePermissions = "users.manage-permissions";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Create = "roles.create";
        public const string Update = "roles.update";
        public const string Delete = "roles.delete";
        public const string AssignPermissions = "roles.assign-permissions";
    }

    public static class Products
    {
        public const string View = "products.view";
        public const string Create = "products.create";
        public const string Update = "products.update";
        public const string Delete = "products.delete";
        public const string Publish = "products.publish";
        public const string Import = "products.import";
    }

    public static class Categories
    {
        public const string View = "categories.view";
        public const string Create = "categories.create";
        public const string Update = "categories.update";
        public const string Delete = "categories.delete";
    }

    public static class Brands
    {
        public const string View = "brands.view";
        public const string Create = "brands.create";
        public const string Update = "brands.update";
        public const string Delete = "brands.delete";
    }

    public static class Inventory
    {
        public const string View = "inventory.view";
        public const string Adjust = "inventory.adjust";
        public const string ViewHistory = "inventory.view-history";
    }

    public static class Orders
    {
        public const string View = "orders.view";
        public const string UpdateStatus = "orders.update-status";
        public const string Cancel = "orders.cancel";
        public const string Refund = "orders.refund";
        public const string ViewInvoice = "orders.view-invoice";
        public const string Export = "orders.export";
        public const string ManageShipments = "orders.manage-shipments";
    }

    public static class Customers
    {
        public const string View = "customers.view";
        public const string Update = "customers.update";
        public const string Delete = "customers.delete";
        public const string Export = "customers.export";
    }

    public static class Reviews
    {
        public const string View = "reviews.view";
        public const string Moderate = "reviews.moderate";
        public const string Reply = "reviews.reply";
        public const string Delete = "reviews.delete";
    }

    public static class Coupons
    {
        public const string View = "coupons.view";
        public const string Create = "coupons.create";
        public const string Update = "coupons.update";
        public const string Delete = "coupons.delete";
    }

    public static class Shipping
    {
        public const string View = "shipping.view";
        public const string Manage = "shipping.manage";
    }

    public static class Content
    {
        public const string View = "content.view";
        public const string Manage = "content.manage";
    }

    public static class Media
    {
        public const string View = "media.view";
        public const string Upload = "media.upload";
        public const string Delete = "media.delete";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
        public const string Send = "notifications.send";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static class Settings
    {
        public const string View = "settings.view";
        public const string Manage = "settings.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    /// <summary>
    /// Every permission constant, discovered by reflection over the nested classes. Reflection
    /// runs once at startup during seeding, never on a request path.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();

    /// <summary>Grouped by module for rendering the admin permission matrix.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByModule { get; } = All
        .GroupBy(p => p.Split('.')[0])
        .ToDictionary(g => g.Key, IReadOnlyList<string> (g) => g.ToList());
}

/// <summary>Seeded role names. Referenced by authorization policy and seeding alike.</summary>
public static class RoleNames
{
    /// <summary>Unrestricted. Bypasses permission evaluation entirely.</summary>
    public const string System = "System";

    /// <summary>Staff. Holds only what has been explicitly granted.</summary>
    public const string Admin = "Admin";

    /// <summary>Storefront shopper, scoped to their own resources.</summary>
    public const string Customer = "Customer";

    public static readonly string[] All = [System, Admin, Customer];

    /// <summary>Roles that may reach the admin area at all, before per-permission checks.</summary>
    public static readonly string[] Staff = [System, Admin];
}

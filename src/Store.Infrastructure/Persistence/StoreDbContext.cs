using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Store.Domain.Carts;
using Store.Domain.Catalog;
using Store.Domain.Content;
using Store.Domain.Customers;
using Store.Domain.Identity;
using Store.Domain.Inventory;
using Store.Domain.Orders;
using Store.Domain.Platform;
using Store.Domain.Promotions;
using Store.Domain.Reviews;
using Store.Domain.Shipping;

namespace Store.Infrastructure.Persistence;

/// <summary>
/// The application's single <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// This <i>is</i> the unit of work. There is deliberately no repository layer wrapping it: a
/// generic repository over EF Core hides <c>Include</c>, projection and split-query control —
/// exactly the tools needed to keep queries fast — and buys nothing in return. Services take the
/// context directly and project to DTOs with <c>Select</c>.
/// </remarks>
public class StoreDbContext(DbContextOptions<StoreDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid, AppUserClaim, AppUserRole, AppUserLogin, AppRoleClaim, AppUserToken>(options)
{
    // ---- Identity & RBAC ----
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // ---- Catalog ----
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();
    public DbSet<ProductOptionValue> ProductOptionValues => Set<ProductOptionValue>();
    public DbSet<VariantOptionValue> VariantOptionValues => Set<VariantOptionValue>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    // ---- Inventory ----
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    // ---- Customers & carts ----
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    // ---- Orders ----
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Payment> Payments => Set<Payment>();

    // ---- Promotions & shipping ----
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponTarget> CouponTargets => Set<CouponTarget>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();

    // ---- Engagement ----
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();

    // ---- Content & platform ----
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // One configuration class per entity, discovered from this assembly. Keeps this file a
        // map of the model rather than a thousand-line wall of fluent calls.
        builder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);

        RenameIdentityTables(builder);
        ApplyDecimalPrecision(builder);
        ApplyClientGeneratedKeys(builder);
    }

    /// <summary>
    /// Declares every <see cref="Guid"/> primary key as client-generated.
    /// </summary>
    /// <remarks>
    /// <see cref="Common.BaseEntity"/> assigns its own UUID v7 in the property initializer, so a
    /// key already holds a value before EF ever sees the entity. EF's default convention for GUID
    /// keys is <c>ValueGeneratedOnAdd</c>, and when it discovers an untracked entity during change
    /// detection it infers state from the key: a non-default key means "this row already exists",
    /// so the entity is marked <c>Modified</c> rather than <c>Added</c>.
    /// <para>
    /// The result is an <c>UPDATE</c> that matches zero rows and throws
    /// <c>DbUpdateConcurrencyException</c> — which is exactly what adding an item to an
    /// already-tracked cart did. It affects <b>any</b> child added through a navigation collection
    /// on a tracked parent, so it is fixed here as a model-wide convention rather than at the one
    /// call site that happened to surface it.
    /// </para>
    /// </remarks>
    private static void ApplyClientGeneratedKeys(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();

            if (key is null || key.Properties.Count != 1)
            {
                continue;
            }

            var property = key.Properties[0];

            if (property.ClrType == typeof(Guid))
            {
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    /// <summary>
    /// Drops the <c>AspNet</c> prefix so Identity tables read like the rest of the schema.
    /// </summary>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<AppUser>().ToTable("Users");
        builder.Entity<AppRole>().ToTable("Roles");
        builder.Entity<AppUserRole>().ToTable("UserRoles");
        builder.Entity<AppUserClaim>().ToTable("UserClaims");
        builder.Entity<AppUserLogin>().ToTable("UserLogins");
        builder.Entity<AppUserToken>().ToTable("UserTokens");
        builder.Entity<AppRoleClaim>().ToTable("RoleClaims");
    }

    /// <summary>
    /// Forces every <see cref="decimal"/> in the model to <c>decimal(18,2)</c>.
    /// </summary>
    /// <remarks>
    /// Without this, EF Core silently defaults undeclared decimals to <c>decimal(18,2)</c> on SQL
    /// Server but warns, and any property that slips through with a different scale becomes a
    /// rounding bug in someone's order total. Applying it model-wide means a new money column
    /// cannot be added wrongly by omission.
    /// </remarks>
    private static void ApplyDecimalPrecision(ModelBuilder builder)
    {
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            // Weight is the one non-money decimal; it needs finer scale than currency.
            if (property.Name.Contains("Weight", StringComparison.Ordinal)
                || property.Name.Contains("UnitValue", StringComparison.Ordinal))
            {
                property.SetPrecision(18);
                property.SetScale(3);
            }
            else
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }
        }
    }
}

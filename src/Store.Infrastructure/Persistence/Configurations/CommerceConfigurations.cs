using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Carts;
using Store.Domain.Customers;
using Store.Domain.Inventory;
using Store.Domain.Promotions;
using Store.Domain.Reviews;
using Store.Domain.Shipping;

namespace Store.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Notes).HasMaxLength(2000);

        b.HasOne(x => x.User)
            .WithOne(x => x.Customer)
            .HasForeignKey<Customer>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.UserId).IsUnique();

        // Admin customer list: "highest lifetime value" and "recently ordered" sorts.
        b.HasIndex(x => x.TotalSpent);
        b.HasIndex(x => x.LastOrderAt);

        b.Ignore(x => x.AverageOrderValue);
    }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("Addresses");
        b.HasKey(x => x.Id);

        b.Property(x => x.Label).HasMaxLength(50);
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        b.Property(x => x.Line1).HasMaxLength(300).IsRequired();
        b.Property(x => x.Line2).HasMaxLength(300);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.City).HasMaxLength(100).IsRequired();
        b.Property(x => x.Region).HasMaxLength(100);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();

        b.HasOne(x => x.Customer)
            .WithMany(x => x.Addresses)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.CustomerId);
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.ToTable("Carts");
        b.HasKey(x => x.Id);

        b.Property(x => x.AnonymousId).HasMaxLength(100);

        b.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Coupon)
            .WithMany()
            .HasForeignKey(x => x.CouponId)
            .OnDelete(DeleteBehavior.SetNull);

        // A customer has at most one cart. Filtered unique, because the column is nullable and
        // SQL Server would otherwise treat every guest cart's NULL as a duplicate.
        b.HasIndex(x => x.CustomerId)
            .IsUnique()
            .HasFilter("[CustomerId] IS NOT NULL")
            .HasDatabaseName("IX_Carts_Customer");

        b.HasIndex(x => x.AnonymousId)
            .IsUnique()
            .HasFilter("[AnonymousId] IS NOT NULL")
            .HasDatabaseName("IX_Carts_Anonymous");

        // Backs the abandoned-cart sweep.
        b.HasIndex(x => x.ExpiresAt);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("CartItems");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Cart)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Makes add-to-cart idempotent at the database level: adding the same variant twice
        // increments the existing row rather than creating a duplicate line.
        b.HasIndex(x => new { x.CartId, x.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("IX_CartItems_Cart_Variant");
    }
}

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> b)
    {
        b.ToTable("WishlistItems");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Customer)
            .WithMany(x => x.WishlistItems)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Wishlist is a toggle, so the pair must be unique.
        b.HasIndex(x => new { x.CustomerId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("IX_WishlistItems_Customer_Product");

        // Back-in-stock and price-drop sweeps scan by these flags.
        b.HasIndex(x => new { x.ProductId, x.NotifyOnRestock });
        b.HasIndex(x => new { x.ProductId, x.NotifyOnPriceDrop });

        b.HasQueryFilter(x => x.Product.DeletedAt == null);
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("InventoryTransactions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000);

        // Restrict: the stock ledger must outlive catalogue edits. Deleting a variant cannot
        // erase the history of goods that physically moved.
        b.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Stock-history view for one variant, newest first.
        b.HasIndex(x => new { x.ProductVariantId, x.CreatedAt })
            .HasDatabaseName("IX_InventoryTransactions_Variant_Date");

        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.OrderId);

        b.HasQueryFilter(x => x.ProductVariant.Product.DeletedAt == null);
    }
}

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable("Coupons");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);

        // Guards the redemption counter: without it, concurrent checkouts can both read
        // UsedCount = 99 against a limit of 100 and both succeed.
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_Coupons_Code");
        b.HasIndex(x => new { x.IsActive, x.StartsAt, x.EndsAt });

        b.Ignore(x => x.IsCurrentlyValid);
    }
}

public class CouponTargetConfiguration : IEntityTypeConfiguration<CouponTarget>
{
    public void Configure(EntityTypeBuilder<CouponTarget> b)
    {
        b.ToTable("CouponTargets");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Coupon)
            .WithMany(x => x.Targets)
            .HasForeignKey(x => x.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.CouponId, x.ProductId });
        b.HasIndex(x => new { x.CouponId, x.CategoryId });
    }
}

public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> b)
    {
        b.ToTable("CouponRedemptions");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Coupon)
            .WithMany(x => x.Redemptions)
            .HasForeignKey(x => x.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves the per-customer usage limit check, which counts these rows rather than
        // trusting a denormalised counter.
        b.HasIndex(x => new { x.CouponId, x.CustomerId })
            .HasDatabaseName("IX_CouponRedemptions_Coupon_Customer");

        b.HasIndex(x => x.OrderId);
    }
}

public class ShippingZoneConfiguration : IEntityTypeConfiguration<ShippingZone>
{
    public void Configure(EntityTypeBuilder<ShippingZone> b)
    {
        b.ToTable("ShippingZones");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.CountryCodes).HasMaxLength(500).IsRequired();
        b.Property(x => x.Regions).HasMaxLength(1000);

        b.HasIndex(x => new { x.IsActive, x.DisplayOrder });
    }
}

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> b)
    {
        b.ToTable("ShippingMethods");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);

        b.HasOne(x => x.ShippingZone)
            .WithMany(x => x.Methods)
            .HasForeignKey(x => x.ShippingZoneId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ShippingZoneId, x.IsActive, x.DisplayOrder });
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("Reviews");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.Property(x => x.AdminReply).HasMaxLength(2000);
        b.Property(x => x.RejectionReason).HasMaxLength(500);

        b.HasOne(x => x.Product)
            .WithMany(x => x.Reviews)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Product reviews tab: approved reviews for one product, newest first.
        b.HasIndex(x => new { x.ProductId, x.Status, x.CreatedAt })
            .HasDatabaseName("IX_Reviews_Product_Status_Date");

        // Admin moderation queue.
        b.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("IX_Reviews_Status_Date");

        // One review per customer per product.
        b.HasIndex(x => new { x.CustomerId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("IX_Reviews_Customer_Product");

        b.HasQueryFilter(x => x.Product.DeletedAt == null);
    }
}

public class ReviewVoteConfiguration : IEntityTypeConfiguration<ReviewVote>
{
    public void Configure(EntityTypeBuilder<ReviewVote> b)
    {
        b.ToTable("ReviewVotes");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Review)
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // Stops the helpful counter being inflated by repeated clicks.
        b.HasIndex(x => new { x.ReviewId, x.CustomerId })
            .IsUnique()
            .HasDatabaseName("IX_ReviewVotes_Review_Customer");

        b.HasQueryFilter(x => x.Review.Product.DeletedAt == null);
    }
}

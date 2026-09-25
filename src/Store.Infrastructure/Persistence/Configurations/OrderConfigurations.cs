using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Orders;

namespace Store.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.CouponCode).HasMaxLength(50);
        b.Property(x => x.ShippingMethodName).HasMaxLength(150);
        b.Property(x => x.CustomerNote).HasMaxLength(1000);
        b.Property(x => x.AdminNote).HasMaxLength(2000);
        b.Property(x => x.CancelReason).HasMaxLength(500);

        // Stops two staff members concurrently transitioning the same order and losing one
        // another's change.
        b.Property(x => x.RowVersion).IsRowVersion();

        // Addresses are owned: their columns live on the Orders row. Reading an order never joins
        // to the customer's current address book, so editing an address later cannot rewrite
        // where a past order was delivered.
        b.OwnsOne(x => x.ShippingAddress, ConfigureAddress("Shipping"));
        b.OwnsOne(x => x.BillingAddress, ConfigureAddress("Billing"));

        // SetNull, not Cascade: orders outlive customer accounts. Deleting a customer must never
        // destroy financial history.
        b.HasOne(x => x.Customer)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.OrderNumber).IsUnique().HasDatabaseName("IX_Orders_Number");

        // Customer order history, newest first.
        b.HasIndex(x => new { x.CustomerId, x.PlacedAt }).HasDatabaseName("IX_Orders_Customer_Date");

        // Admin work queue, filtered by status.
        b.HasIndex(x => new { x.Status, x.PlacedAt }).HasDatabaseName("IX_Orders_Status_Date");
        b.HasIndex(x => new { x.PaymentStatus, x.PlacedAt });

        // Guest order lookup by email, and sales reporting over a date range.
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.PlacedAt).HasDatabaseName("IX_Orders_PlacedAt");

        b.Ignore(x => x.TotalItems);
        b.Ignore(x => x.IsCancellable);
    }

    /// <summary>Owned-address column mapping, shared by the shipping and billing copies.</summary>
    private static Action<OwnedNavigationBuilder<Order, OrderAddress>> ConfigureAddress(string prefix) =>
        a =>
        {
            a.Property(p => p.FullName).HasColumnName($"{prefix}FullName").HasMaxLength(200).IsRequired();
            a.Property(p => p.Phone).HasColumnName($"{prefix}Phone").HasMaxLength(30).IsRequired();
            a.Property(p => p.Line1).HasColumnName($"{prefix}Line1").HasMaxLength(300).IsRequired();
            a.Property(p => p.Line2).HasColumnName($"{prefix}Line2").HasMaxLength(300);
            a.Property(p => p.District).HasColumnName($"{prefix}District").HasMaxLength(100);
            a.Property(p => p.City).HasColumnName($"{prefix}City").HasMaxLength(100).IsRequired();
            a.Property(p => p.Region).HasColumnName($"{prefix}Region").HasMaxLength(100);
            a.Property(p => p.PostalCode).HasColumnName($"{prefix}PostalCode").HasMaxLength(20);
            a.Property(p => p.CountryCode).HasColumnName($"{prefix}CountryCode").HasMaxLength(2).IsRequired();
        };
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItems");
        b.HasKey(x => x.Id);

        // Snapshot columns. These are required because an order line must remain fully
        // self-describing even if the product row is later deleted.
        b.Property(x => x.ProductName).HasMaxLength(400).IsRequired();
        b.Property(x => x.VariantName).HasMaxLength(200);
        b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.Property(x => x.ProductSlug).HasMaxLength(450);

        b.HasOne(x => x.Order)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: the variant reference is a convenience for reordering and reporting. Losing it
        // must not damage the order.
        b.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.OrderId);

        // "Best selling products" reporting, and the verified-purchase check when a customer
        // submits a review.
        b.HasIndex(x => x.ProductId).HasDatabaseName("IX_OrderItems_Product");
        b.HasIndex(x => x.ProductVariantId);
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("OrderStatusHistory");
        b.HasKey(x => x.Id);

        b.Property(x => x.Note).HasMaxLength(1000);

        b.HasOne(x => x.Order)
            .WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Backs the customer-facing tracking timeline.
        b.HasIndex(x => new { x.OrderId, x.CreatedAt });
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("Shipments");
        b.HasKey(x => x.Id);

        b.Property(x => x.Carrier).HasMaxLength(100).IsRequired();
        b.Property(x => x.TrackingNumber).HasMaxLength(100);
        b.Property(x => x.TrackingUrl).HasMaxLength(1000);
        b.Property(x => x.Note).HasMaxLength(1000);

        b.HasOne(x => x.Order)
            .WithMany(x => x.Shipments)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.TrackingNumber);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.HasKey(x => x.Id);

        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.ProviderReference).HasMaxLength(200);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.Property(x => x.Note).HasMaxLength(1000);

        b.HasOne(x => x.Order)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.ProviderReference);
    }
}

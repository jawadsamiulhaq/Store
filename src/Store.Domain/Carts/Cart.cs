using Store.Domain.Catalog;
using Store.Domain.Common;
using Store.Domain.Customers;
using Store.Domain.Promotions;

namespace Store.Domain.Carts;

/// <summary>
/// A shopping cart. Exactly one of <see cref="CustomerId"/> or <see cref="AnonymousId"/> is set.
/// </summary>
/// <remarks>
/// Guests shop under an <see cref="AnonymousId"/> held in a cookie. On login the guest cart is
/// merged into the customer's cart rather than replacing it, so items added before signing in
/// are never silently lost — a common way to lose a sale.
/// </remarks>
public class Cart : BaseEntity
{
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Cookie-held identifier for a guest cart.</summary>
    public string? AnonymousId { get; set; }

    public Guid? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Abandoned guest carts are swept after this instant to keep the table small.</summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);

    public ICollection<CartItem> Items { get; set; } = [];
}

public class CartItem : BaseEntity
{
    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>
    /// Price captured when the item was added. Totals are always recalculated from the live
    /// variant price at checkout; this exists only so the UI can tell the shopper
    /// "the price of this item changed while it was in your cart".
    /// </summary>
    public decimal UnitPriceSnapshot { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

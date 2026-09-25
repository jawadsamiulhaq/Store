using Store.Domain.Common;
using Store.Domain.Enums;

namespace Store.Domain.Promotions;

/// <summary>
/// A discount code. Every limit is enforced server-side at checkout; the storefront's preview of
/// a discount is advisory only.
/// </summary>
public class Coupon : AuditableEntity
{
    /// <summary>Stored and compared upper-case so <c>save10</c> and <c>SAVE10</c> are one coupon.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    /// <summary>Percentage points for <see cref="DiscountType.Percentage"/>, else an amount.</summary>
    public decimal Value { get; set; }

    /// <summary>Basket must reach this subtotal before the coupon applies.</summary>
    public decimal? MinOrderAmount { get; set; }

    /// <summary>Caps a percentage discount, e.g. "20% off, up to $100".</summary>
    public decimal? MaxDiscountAmount { get; set; }

    public CouponScope Scope { get; set; } = CouponScope.EntireOrder;

    /// <summary>Product or category ids the coupon is restricted to, when scoped.</summary>
    public ICollection<CouponTarget> Targets { get; set; } = [];

    /// <summary>Total redemptions allowed across all customers. Null means unlimited.</summary>
    public int? UsageLimit { get; set; }

    /// <summary>Redemptions allowed per customer. Null means unlimited.</summary>
    public int? UsageLimitPerCustomer { get; set; }

    /// <summary>Maintained redemption counter, checked against <see cref="UsageLimit"/>.</summary>
    public int UsedCount { get; set; }

    /// <summary>Restricts the coupon to customers who have never ordered.</summary>
    public bool FirstOrderOnly { get; set; }

    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Guards the redemption counter against concurrent checkouts exceeding the limit.</summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<CouponRedemption> Redemptions { get; set; } = [];

    /// <summary>Window and enable checks only. Basket-dependent rules live in the discount service.</summary>
    public bool IsCurrentlyValid
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            return IsActive
                && (StartsAt is null || StartsAt <= now)
                && (EndsAt is null || EndsAt >= now)
                && (UsageLimit is null || UsedCount < UsageLimit);
        }
    }
}

/// <summary>Restricts a scoped coupon to a specific product or category.</summary>
public class CouponTarget : BaseEntity
{
    public Guid CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
}

/// <summary>
/// One use of a coupon. Rows are the source of truth for per-customer limits — counting these is
/// reliable, whereas trusting a denormalised per-customer counter is not.
/// </summary>
public class CouponRedemption : BaseEntity
{
    public Guid CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public Guid OrderId { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>Actual money discounted, after caps were applied.</summary>
    public decimal DiscountAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

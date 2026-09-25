using Store.Domain.Common;
using Store.Domain.Enums;

namespace Store.Domain.Shipping;

/// <summary>
/// A geographic area with its own delivery options. For a Hong Kong grocery the meaningful
/// zones are the territories — Kowloon, Hong Kong Island, New Territories, outlying islands —
/// which genuinely differ in cost and lead time.
/// </summary>
public class ShippingZone : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>ISO country codes this zone covers, comma-separated.</summary>
    public string CountryCodes { get; set; } = "HK";

    /// <summary>Matching regions/districts, comma-separated. Empty means the whole country.</summary>
    public string? Regions { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ShippingMethod> Methods { get; set; } = [];
}

/// <summary>A delivery option within a zone, priced by <see cref="RateType"/>.</summary>
public class ShippingMethod : AuditableEntity
{
    public Guid ShippingZoneId { get; set; }
    public ShippingZone ShippingZone { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ShippingRateType RateType { get; set; } = ShippingRateType.Flat;

    /// <summary>Base charge. Zero for pickup.</summary>
    public decimal BaseRate { get; set; }

    /// <summary>Subtotal at which shipping becomes free, for <see cref="ShippingRateType.FreeOverAmount"/>.</summary>
    public decimal? FreeOverAmount { get; set; }

    /// <summary>Added per kilogram, for <see cref="ShippingRateType.WeightBased"/>.</summary>
    public decimal? RatePerKg { get; set; }

    /// <summary>Weight included in <see cref="BaseRate"/> before per-kg charging starts.</summary>
    public decimal? BaseWeightKg { get; set; }

    public int EstimatedDaysMin { get; set; } = 1;
    public int EstimatedDaysMax { get; set; } = 3;

    /// <summary>Hides the method for baskets below this weight/size threshold where it is impractical.</summary>
    public decimal? MaxWeightKg { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>Calculates the charge for a basket. Pure and total — no I/O, so it is trivially testable.</summary>
    public decimal CalculateRate(decimal orderSubtotal, decimal totalWeightKg)
    {
        if (RateType == ShippingRateType.Pickup)
            return 0m;

        if (RateType == ShippingRateType.FreeOverAmount
            && FreeOverAmount.HasValue
            && orderSubtotal >= FreeOverAmount.Value)
        {
            return 0m;
        }

        if (RateType != ShippingRateType.WeightBased)
            return BaseRate;

        var included = BaseWeightKg ?? 0m;
        var billable = Math.Max(0m, totalWeightKg - included);
        return BaseRate + billable * (RatePerKg ?? 0m);
    }
}

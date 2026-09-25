using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Domain.Content;
using Store.Domain.Enums;
using Store.Domain.Platform;
using Store.Domain.Shipping;

namespace Store.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds store settings, shipping zones/methods and the mandatory content pages.
/// </summary>
/// <remarks>
/// Each section inserts only what is missing, keyed on a natural key, so startup seeding never
/// overwrites a value an operator has since edited in the admin UI.
/// </remarks>
public sealed class PlatformSeeder(StoreDbContext db, ILogger<PlatformSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedSettingsAsync(ct);
        await SeedShippingAsync(ct);
        await SeedContentPagesAsync(ct);
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var defaults = new[]
        {
            // ---- Store identity ----
            S("store.name", "Waqas Provision Store", "Store", isPublic: true, display: "Store name"),
            S("store.tagline", "Your neighbourhood pantry, delivered across Hong Kong", "Store", isPublic: true, display: "Tagline"),
            S("store.email", "hello@waqasprovisionstore.com", "Store", isPublic: true, display: "Contact email"),
            S("store.phone", "+852 0000 0000", "Store", isPublic: true, display: "Contact phone"),
            S("store.address", "Ngau Chi Wan Market, Choi Hung, Kowloon, Hong Kong", "Store", isPublic: true, display: "Shop address"),
            S("store.opening-hours", "Mon–Sun, 09:00–21:00", "Store", isPublic: true, display: "Opening hours"),

            // ---- Commerce ----
            S("store.currency", "HKD", "Store", isPublic: true, display: "Currency"),
            S("store.currency-symbol", "HK$", "Store", isPublic: true, display: "Currency symbol"),
            S("store.country", "HK", "Store", isPublic: true, display: "Country"),

            // ---- Checkout rules ----
            S("checkout.min-order-amount", "50", "Checkout", "number", isPublic: true, display: "Minimum order amount"),
            S("checkout.free-shipping-threshold", "300", "Checkout", "number", isPublic: true, display: "Free shipping over"),
            S("checkout.guest-enabled", "true", "Checkout", "boolean", isPublic: true, display: "Allow guest checkout"),
            S("checkout.cod-enabled", "true", "Checkout", "boolean", isPublic: true, display: "Cash on delivery"),

            // ---- Catalogue ----
            S("catalog.page-size", "24", "Catalog", "number", isPublic: true, display: "Products per page"),
            S("catalog.low-stock-threshold", "10", "Catalog", "number", display: "Default low-stock threshold"),
            S("catalog.reviews-require-approval", "true", "Catalog", "boolean", display: "Moderate reviews before publishing"),
            S("catalog.reviews-require-purchase", "false", "Catalog", "boolean", display: "Only verified buyers may review"),

            // ---- Social ----
            S("social.facebook", "", "Social", isPublic: true, display: "Facebook URL"),
            S("social.instagram", "", "Social", isPublic: true, display: "Instagram URL"),
            S("social.whatsapp", "", "Social", isPublic: true, display: "WhatsApp number"),

            // ---- Email (never public: these reach the storefront bootstrap payload otherwise) ----
            S("email.from-name", "Waqas Provision Store", "Email", display: "Sender name"),
            S("email.from-address", "no-reply@waqasprovisionstore.com", "Email", display: "Sender address"),
            S("email.order-confirmation-enabled", "true", "Email", "boolean", display: "Send order confirmations"),
            S("email.low-stock-alerts-enabled", "true", "Email", "boolean", display: "Send low-stock alerts")
        };

        var existingKeys = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        var missing = defaults.Where(s => !existingKeys.Contains(s.Key)).ToList();

        if (missing.Count == 0)
            return;

        db.Settings.AddRange(missing);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} settings", missing.Count);
    }

    /// <summary>
    /// Seeds the Hong Kong delivery zones. The territories genuinely differ in cost and lead
    /// time, which is why they are separate zones rather than one flat national rate.
    /// </summary>
    private async Task SeedShippingAsync(CancellationToken ct)
    {
        if (await db.ShippingZones.AnyAsync(ct))
            return;

        var kowloon = new ShippingZone
        {
            Name = "Kowloon",
            Description = "Kowloon peninsula — the shop's home territory.",
            CountryCodes = "HK",
            Regions = "Kowloon",
            DisplayOrder = 0,
            Methods =
            [
                new ShippingMethod
                {
                    Name = "Standard delivery",
                    Description = "Delivered by Gogo Van within 1–2 days.",
                    RateType = ShippingRateType.FreeOverAmount,
                    BaseRate = 30m,
                    FreeOverAmount = 300m,
                    EstimatedDaysMin = 1,
                    EstimatedDaysMax = 2,
                    DisplayOrder = 0
                },
                new ShippingMethod
                {
                    Name = "Same-day delivery",
                    Description = "Order before 14:00 for delivery the same evening.",
                    RateType = ShippingRateType.Flat,
                    BaseRate = 70m,
                    EstimatedDaysMin = 0,
                    EstimatedDaysMax = 1,
                    DisplayOrder = 1
                },
                new ShippingMethod
                {
                    Name = "Collect in store",
                    Description = "Pick up at Ngau Chi Wan Market, Choi Hung.",
                    RateType = ShippingRateType.Pickup,
                    BaseRate = 0m,
                    EstimatedDaysMin = 0,
                    EstimatedDaysMax = 1,
                    DisplayOrder = 2
                }
            ]
        };

        var hkIsland = new ShippingZone
        {
            Name = "Hong Kong Island",
            CountryCodes = "HK",
            Regions = "Hong Kong Island",
            DisplayOrder = 1,
            Methods =
            [
                new ShippingMethod
                {
                    Name = "Standard delivery",
                    Description = "Delivered by Gogo Van within 1–3 days.",
                    RateType = ShippingRateType.FreeOverAmount,
                    BaseRate = 45m,
                    FreeOverAmount = 400m,
                    EstimatedDaysMin = 1,
                    EstimatedDaysMax = 3,
                    DisplayOrder = 0
                }
            ]
        };

        var newTerritories = new ShippingZone
        {
            Name = "New Territories",
            CountryCodes = "HK",
            Regions = "New Territories",
            DisplayOrder = 2,
            Methods =
            [
                new ShippingMethod
                {
                    Name = "Standard delivery",
                    Description = "Delivered within 2–4 days.",
                    RateType = ShippingRateType.FreeOverAmount,
                    BaseRate = 60m,
                    FreeOverAmount = 500m,
                    EstimatedDaysMin = 2,
                    EstimatedDaysMax = 4,
                    DisplayOrder = 0
                },
                new ShippingMethod
                {
                    Name = "Bulk / heavy order",
                    Description = "For large sack and case orders. Priced by weight.",
                    RateType = ShippingRateType.WeightBased,
                    BaseRate = 60m,
                    BaseWeightKg = 10m,
                    RatePerKg = 4m,
                    EstimatedDaysMin = 2,
                    EstimatedDaysMax = 5,
                    DisplayOrder = 1
                }
            ]
        };

        db.ShippingZones.AddRange(kowloon, hkIsland, newTerritories);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded 3 shipping zones");
    }

    /// <summary>
    /// Creates the pages the footer and checkout link to by slug. Marked as system pages so they
    /// cannot be deleted out from under those links.
    /// </summary>
    private async Task SeedContentPagesAsync(CancellationToken ct)
    {
        var pages = new[]
        {
            ("about", "About us", 0),
            ("contact", "Contact", 1),
            ("delivery", "Delivery information", 2),
            ("returns", "Returns & refunds", 3),
            ("privacy", "Privacy policy", 4),
            ("terms", "Terms & conditions", 5),
            ("faq", "Frequently asked questions", 6)
        };

        var existingSlugs = await db.ContentPages.Select(p => p.Slug).ToListAsync(ct);
        var missing = pages.Where(p => !existingSlugs.Contains(p.Item1)).ToList();

        if (missing.Count == 0)
            return;

        db.ContentPages.AddRange(missing.Select(p => new ContentPage
        {
            Slug = p.Item1,
            Title = p.Item2,
            // Placeholder copy: staff edit these in admin. Deliberately not lorem ipsum, so an
            // unedited page is still honest rather than gibberish.
            Body = $"<p>This page has not been written yet. Edit “{p.Item2}” in the admin area to publish it.</p>",
            Status = ContentStatus.Draft,
            ShowInFooter = true,
            DisplayOrder = p.Item3,
            IsSystemPage = true,
            MetaTitle = p.Item2
        }));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} content pages", missing.Count);
    }

    private static Setting S(
        string key,
        string value,
        string group,
        string dataType = "string",
        bool isPublic = false,
        string? display = null) =>
        new()
        {
            Key = key,
            Value = value,
            Group = group,
            DataType = dataType,
            IsPublic = isPublic,
            DisplayName = display
        };
}

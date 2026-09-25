using Store.Domain.Common;
using Store.Domain.Enums;
using Store.Domain.Reviews;

namespace Store.Domain.Catalog;

/// <summary>
/// A sellable product. Holds identity, merchandising and SEO only — <b>price and stock live on
/// <see cref="ProductVariant"/></b>, because a grocery sells the same item in 500 g, 1 kg and 5 kg.
/// The legacy store had one price and one stock figure per product and a <c>unit</c> column that
/// read <c>"piece"</c> for all 4,207 rows, which made pack sizes impossible to express.
/// </summary>
public class Product : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>One-line summary for cards and search results.</summary>
    public string? ShortDescription { get; set; }

    /// <summary>Full description, HTML-sanitised on write.</summary>
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    // ---- Merchandising flags (the legacy store's hero/featured/trending curation, kept) ----
    public bool IsFeatured { get; set; }
    public int FeaturedOrder { get; set; }
    public bool IsTrending { get; set; }
    public int TrendingOrder { get; set; }
    public bool IsHero { get; set; }
    public int HeroOrder { get; set; }

    /// <summary>Free-text flash such as "New" or "Best seller", rendered as a corner ribbon.</summary>
    public string? Badge { get; set; }

    // ---- Denormalised aggregates ----------------------------------------------------------
    // These are maintained inside the same transaction as the writes that change them
    // (variant price/stock edits, review approval). They exist so that the catalogue grid —
    // the single most-requested query in the whole application — can sort by price, filter by
    // availability and show a star rating without aggregating child tables at read time.

    /// <summary>Lowest effective price across active variants. Drives price sort and facets.</summary>
    public decimal MinPrice { get; set; }

    /// <summary>Highest effective price across active variants. Drives "from X to Y" display.</summary>
    public decimal MaxPrice { get; set; }

    /// <summary>Highest <c>CompareAtPrice</c>, used to render the struck-through original.</summary>
    public decimal? MaxCompareAtPrice { get; set; }

    /// <summary>True when any active variant is sellable. Drives the in-stock filter.</summary>
    public bool InStock { get; set; }

    /// <summary>Sum of sellable stock across active variants.</summary>
    public int TotalStock { get; set; }

    /// <summary>Mean of approved review ratings, 0 when unreviewed.</summary>
    public double RatingAverage { get; set; }

    /// <summary>Count of approved reviews.</summary>
    public int RatingCount { get; set; }

    /// <summary>Lifetime units sold. Backs the "best selling" sort without touching OrderItems.</summary>
    public int SalesCount { get; set; }

    public int ViewCount { get; set; }

    // ---- SEO ----
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = [];
    public ICollection<ProductOption> Options { get; set; } = [];
    public ICollection<ProductImage> Images { get; set; } = [];
    public ICollection<ProductTag> ProductTags { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];

    public bool IsOnSale => MaxCompareAtPrice.HasValue && MaxCompareAtPrice > MinPrice;
}

/// <summary>
/// A specific purchasable configuration of a product — the thing that actually carries a SKU,
/// a price and a stock level. Every product has at least one variant; simple products get a
/// single default variant so there is exactly one code path for pricing and inventory.
/// </summary>
public class ProductVariant : AuditableEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Human-facing variant label, e.g. "1 kg". Null for a simple product's default variant.</summary>
    public string? Name { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    /// <summary>Selling price.</summary>
    public decimal Price { get; set; }

    /// <summary>Original price, shown struck through when higher than <see cref="Price"/>.</summary>
    public decimal? CompareAtPrice { get; set; }

    /// <summary>Buy price. Admin/reporting only — never serialised to the storefront.</summary>
    public decimal? CostPrice { get; set; }

    // ---- Inventory ----
    /// <summary>Physical units on hand.</summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Units held for in-flight checkouts. Sellable stock is
    /// <see cref="StockQuantity"/> − <see cref="ReservedQuantity"/>, which is what prevents two
    /// shoppers from buying the same last unit concurrently.
    /// </summary>
    public int ReservedQuantity { get; set; }

    public int LowStockThreshold { get; set; } = 10;
    public bool TrackInventory { get; set; } = true;
    public bool AllowBackorder { get; set; }

    // ---- Physical attributes (drive weight-based shipping) ----
    public decimal? WeightGrams { get; set; }

    /// <summary>Unit of sale: piece, kg, g, litre, ml, pack, box, dozen.</summary>
    public string Unit { get; set; } = "piece";

    /// <summary>Quantity of <see cref="Unit"/> in this variant, e.g. 500 for a 500 g pack.</summary>
    public decimal? UnitValue { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>Guards against lost updates when two orders decrement the same stock row.</summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<VariantOptionValue> OptionValues { get; set; } = [];
    public ICollection<ProductImage> Images { get; set; } = [];

    public int AvailableQuantity => Math.Max(0, StockQuantity - ReservedQuantity);
    public bool IsSellable => !TrackInventory || AllowBackorder || AvailableQuantity > 0;
    public bool IsLowStock => TrackInventory && AvailableQuantity <= LowStockThreshold;

    /// <summary>Price per base unit, so shoppers can compare a 500 g pack against a 1 kg pack.</summary>
    public decimal? PricePerUnit => UnitValue is > 0 ? Price / UnitValue.Value : null;
}

/// <summary>An axis of variation for a product, e.g. "Size" or "Flavour".</summary>
public class ProductOption : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public ICollection<ProductOptionValue> Values { get; set; } = [];
}

/// <summary>One choice along an option axis, e.g. "500 g".</summary>
public class ProductOptionValue : BaseEntity
{
    public Guid OptionId { get; set; }
    public ProductOption Option { get; set; } = null!;

    public string Value { get; set; } = string.Empty;

    /// <summary>Optional swatch colour for colour-type options.</summary>
    public string? HexColor { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<VariantOptionValue> VariantValues { get; set; } = [];
}

/// <summary>
/// Joins a variant to the option values that define it. A variant with rows for
/// (Size = 1 kg) and (Grind = Fine) is the 1 kg fine-ground SKU.
/// </summary>
public class VariantOptionValue
{
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    public Guid OptionValueId { get; set; }
    public ProductOptionValue OptionValue { get; set; } = null!;
}

/// <summary>
/// A product image. Dimensions and a blur placeholder are stored, not guessed, so the browser
/// can reserve exact space before the bytes arrive — that is what keeps CLS at zero.
/// The legacy store hotlinked unsized Firebase and Unsplash originals.
/// </summary>
public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Set when the image belongs to one specific variant rather than the product.</summary>
    public Guid? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    /// <summary>Full-size original.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Grid-sized derivative, requested by cards so listings never download originals.</summary>
    public string? ThumbnailUrl { get; set; }

    public string? AltText { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Tiny base64 LQIP rendered behind the image while it loads.</summary>
    public string? BlurHash { get; set; }

    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
}

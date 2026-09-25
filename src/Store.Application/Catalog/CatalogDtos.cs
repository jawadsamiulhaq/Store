using Store.Application.Common;
using Store.Domain.Enums;

namespace Store.Application.Catalog;

// ============================================================================================
// Categories
// ============================================================================================

/// <remarks>
/// Carries <see cref="MetaTitle"/> and <see cref="MetaDescription"/> even though the storefront
/// does not render them from this shape. <see cref="UpdateCategoryRequest"/> writes both, so an
/// admin editor that could not read them back would send empty strings on every save and silently
/// erase a category's SEO the first time anyone changed its display order.
/// </remarks>
public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    string? IconName,
    Guid? ParentId,
    int Depth,
    int DisplayOrder,
    bool IsActive,
    bool ShowInMenu,
    int ProductCount,
    string? MetaTitle,
    string? MetaDescription);

/// <summary>A category with its children, for rendering the navigation tree in one pass.</summary>
public sealed record CategoryTreeDto(
    Guid Id,
    string Name,
    string Slug,
    string? ImageUrl,
    string? IconName,
    int ProductCount,
    IReadOnlyList<CategoryTreeDto> Children);

public sealed record CreateCategoryRequest(
    string Name,
    string? Slug,
    string? Description,
    string? ImageUrl,
    string? IconName,
    Guid? ParentId,
    int DisplayOrder,
    bool IsActive,
    bool ShowInMenu,
    string? MetaTitle,
    string? MetaDescription);

public sealed record UpdateCategoryRequest(
    string Name,
    string? Slug,
    string? Description,
    string? ImageUrl,
    string? IconName,
    Guid? ParentId,
    int DisplayOrder,
    bool IsActive,
    bool ShowInMenu,
    string? MetaTitle,
    string? MetaDescription);

// ============================================================================================
// Brands
// ============================================================================================

public sealed record BrandDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? CountryOfOrigin,
    int DisplayOrder,
    bool IsActive,
    bool IsFeatured,
    int ProductCount);

public sealed record CreateBrandRequest(
    string Name,
    string? Slug,
    string? Description,
    string? LogoUrl,
    string? CountryOfOrigin,
    int DisplayOrder,
    bool IsActive,
    bool IsFeatured);

public sealed record UpdateBrandRequest(
    string Name,
    string? Slug,
    string? Description,
    string? LogoUrl,
    string? CountryOfOrigin,
    int DisplayOrder,
    bool IsActive,
    bool IsFeatured);

// ============================================================================================
// Images
// ============================================================================================

/// <summary>
/// An image with everything the browser needs to lay it out before the bytes arrive.
/// </summary>
/// <remarks>
/// <see cref="Width"/>/<see cref="Height"/> let the markup reserve the exact box, and
/// <see cref="BlurHash"/> fills it meanwhile — together that is how CLS stays at zero. The legacy
/// store shipped bare URLs with no dimensions, so every image load shifted the page.
/// </remarks>
public sealed record ImageDto(
    Guid Id,
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int Width,
    int Height,
    string? BlurHash,
    bool IsPrimary,
    int DisplayOrder);

// ============================================================================================
// Products — storefront
// ============================================================================================

/// <summary>
/// The product grid card. Deliberately minimal.
/// </summary>
/// <remarks>
/// This is the single highest-volume payload in the application — 24 of these per catalogue page.
/// It carries no description body, no variant list, no review text: only what a card renders.
/// Every field is filled by a projection evaluated in SQL, so a page costs one query.
/// <para>
/// For contrast, the legacy store returned the full catalogue (4,207 products, every field,
/// serialised twice) on every request: 11.3 MB in 50 s.
/// </para>
/// </remarks>
public sealed record ProductCardDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string? ImageUrl,
    string? ThumbnailUrl,
    string? BlurHash,
    int ImageWidth,
    int ImageHeight,
    decimal MinPrice,
    decimal MaxPrice,
    decimal? CompareAtPrice,
    bool InStock,
    double RatingAverage,
    int RatingCount,
    string? Badge,
    string? BrandName,
    string? CategoryName,
    string? CategorySlug,
    int VariantCount,
    string Unit)
{
    public bool HasPriceRange => MaxPrice > MinPrice;

    /// <summary>Whole-percent saving, for the "-25%" flash. Null when not discounted.</summary>
    public int? DiscountPercent =>
        CompareAtPrice > MinPrice && CompareAtPrice > 0
            ? (int)Math.Round((CompareAtPrice.Value - MinPrice) / CompareAtPrice.Value * 100)
            : null;
}

/// <summary>Full product detail, loaded only on the product page.</summary>
public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string? Description,
    string? Badge,
    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,
    Guid? BrandId,
    string? BrandName,
    string? BrandSlug,
    decimal MinPrice,
    decimal MaxPrice,
    bool InStock,
    int TotalStock,
    double RatingAverage,
    int RatingCount,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<ImageDto> Images,
    IReadOnlyList<ProductOptionDto> Options,
    IReadOnlyList<ProductVariantDto> Variants,
    IReadOnlyList<string> Tags,
    IReadOnlyList<BreadcrumbDto> Breadcrumbs);

public sealed record BreadcrumbDto(string Name, string Slug);

public sealed record ProductOptionDto(
    Guid Id,
    string Name,
    int DisplayOrder,
    IReadOnlyList<ProductOptionValueDto> Values);

public sealed record ProductOptionValueDto(Guid Id, string Value, string? HexColor, int DisplayOrder);

public sealed record ProductVariantDto(
    Guid Id,
    string? Name,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    int AvailableQuantity,
    bool IsSellable,
    bool IsLowStock,
    string Unit,
    decimal? UnitValue,
    decimal? PricePerUnit,
    decimal? WeightGrams,
    bool IsDefault,
    int DisplayOrder,
    IReadOnlyList<Guid> OptionValueIds);

// ============================================================================================
// Products — admin
// ============================================================================================

/// <summary>Admin grid row. Carries operational data a shopper never sees (cost, stock, status).</summary>
public sealed record AdminProductListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? ThumbnailUrl,
    string? CategoryName,
    string? BrandName,
    ProductStatus Status,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalStock,
    int VariantCount,
    bool IsFeatured,
    bool IsTrending,
    bool HasLowStock,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

// --------------------------------------------------------------------------------------------
// Admin editor
// --------------------------------------------------------------------------------------------

/// <summary>
/// Everything the product editor needs to load a product for editing.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ProductDetailDto"/>. That shape is the storefront's: it
/// exposes <c>AvailableQuantity</c> but not <c>StockQuantity</c>, and it carries no cost price, no
/// status, no per-variant active flag and no merchandising orders — all of which an editor must
/// round-trip and none of which a shopper may see. Reusing it would have meant either leaking
/// cost prices to the storefront or an editor that silently wiped the fields it could not read.
/// </remarks>
public sealed record AdminProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string? Description,
    Guid? CategoryId,
    Guid? BrandId,
    ProductStatus Status,
    string? Badge,
    bool IsFeatured,
    int FeaturedOrder,
    bool IsTrending,
    int TrendingOrder,
    bool IsHero,
    int HeroOrder,
    string? MetaTitle,
    string? MetaDescription,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AdminProductOptionDto> Options,
    IReadOnlyList<AdminVariantDto> Variants,
    IReadOnlyList<ImageDto> Images);

public sealed record AdminProductOptionDto(
    Guid Id,
    string Name,
    int DisplayOrder,
    IReadOnlyList<AdminProductOptionValueDto> Values);

public sealed record AdminProductOptionValueDto(Guid Id, string Value, string? HexColor, int DisplayOrder);

/// <summary>
/// A variant as the editor sees it: physical stock rather than sellable stock, plus cost.
/// </summary>
/// <remarks>
/// <see cref="ReservedQuantity"/> is included read-only so the editor can explain the difference
/// between what is on the shelf and what can still be sold, rather than appearing to disagree with
/// the storefront.
/// </remarks>
public sealed record AdminVariantDto(
    Guid Id,
    string? Name,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    int StockQuantity,
    int ReservedQuantity,
    int LowStockThreshold,
    bool TrackInventory,
    bool AllowBackorder,
    decimal? WeightGrams,
    string Unit,
    decimal? UnitValue,
    bool IsDefault,
    bool IsActive,
    int DisplayOrder,
    // Whether this variant has ever been ordered or stock-adjusted. Drives whether the editor
    // offers to remove it, since history makes a hard delete impossible.
    bool HasHistory,
    IReadOnlyList<VariantOptionSelection> OptionValues);

/// <summary>
/// One coordinate of a variant along an option axis, by label rather than by id.
/// </summary>
/// <remarks>
/// Labels, not ids, because the editor builds variants from the option values the user is typing
/// in the same submission — those values have no id yet. The server resolves each selection
/// against the options it has just saved, which also means a renamed value carries its variants
/// with it instead of orphaning them.
/// </remarks>
public sealed record VariantOptionSelection(string Option, string Value);

public sealed record SaveProductOptionRequest(
    Guid? Id,
    string Name,
    int DisplayOrder,
    IReadOnlyList<SaveProductOptionValueRequest> Values);

public sealed record SaveProductOptionValueRequest(Guid? Id, string Value, string? HexColor, int DisplayOrder);

/// <summary>
/// A variant in a save. <see cref="Id"/> null means "create"; an id means "update that one".
/// </summary>
/// <remarks>
/// <see cref="StockQuantity"/> is honoured only when creating a variant, where it is the opening
/// balance and is written to the stock ledger. On an existing variant it is ignored: stock moves
/// through <c>/api/admin/inventory/adjust</c>, which records who changed it and why. Letting the
/// product form set it directly would silently desynchronise the ledger from the stock column,
/// and the ledger is the thing that has to be able to explain every unit.
/// </remarks>
public sealed record SaveVariantRequest(
    Guid? Id,
    string? Name,
    string? Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    int StockQuantity,
    int LowStockThreshold,
    bool TrackInventory,
    bool AllowBackorder,
    decimal? WeightGrams,
    string Unit,
    decimal? UnitValue,
    bool IsDefault,
    bool IsActive,
    int DisplayOrder,
    IReadOnlyList<VariantOptionSelection>? OptionValues);

public sealed record SaveProductImageRequest(
    Guid? Id,
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int Width,
    int Height,
    string? BlurHash,
    bool IsPrimary,
    int DisplayOrder);

public sealed record CreateProductRequest(
    string Name,
    string? Slug,
    string? ShortDescription,
    string? Description,
    Guid? CategoryId,
    Guid? BrandId,
    ProductStatus Status,
    string? Badge,
    bool IsFeatured,
    bool IsTrending,
    bool IsHero,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<CreateVariantRequest> Variants,
    IReadOnlyList<CreateProductImageRequest>? Images);

/// <summary>
/// A full product save from the admin editor.
/// </summary>
/// <remarks>
/// <see cref="Tags"/>, <see cref="Options"/>, <see cref="Variants"/> and <see cref="Images"/> are
/// each <b>null-means-leave-alone, non-null-means-replace-wholesale</b>. A caller that only
/// changes the name sends null for all four and touches nothing else; the editor, which always
/// renders the whole product, sends all four.
/// <para>
/// Sending an empty list is therefore meaningfully different from sending null — for variants it
/// is rejected outright, since a product with no variants has no price and no stock.
/// </para>
/// </remarks>
public sealed record UpdateProductRequest(
    string Name,
    string? Slug,
    string? ShortDescription,
    string? Description,
    Guid? CategoryId,
    Guid? BrandId,
    ProductStatus Status,
    string? Badge,
    bool IsFeatured,
    int FeaturedOrder,
    bool IsTrending,
    int TrendingOrder,
    bool IsHero,
    int HeroOrder,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<SaveProductOptionRequest>? Options = null,
    IReadOnlyList<SaveVariantRequest>? Variants = null,
    IReadOnlyList<SaveProductImageRequest>? Images = null);

public sealed record CreateVariantRequest(
    string? Name,
    string? Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    int StockQuantity,
    int LowStockThreshold,
    bool TrackInventory,
    bool AllowBackorder,
    decimal? WeightGrams,
    string Unit,
    decimal? UnitValue,
    bool IsDefault,
    int DisplayOrder);

public sealed record UpdateVariantRequest(
    string? Name,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    int LowStockThreshold,
    bool TrackInventory,
    bool AllowBackorder,
    decimal? WeightGrams,
    string Unit,
    decimal? UnitValue,
    bool IsActive,
    int DisplayOrder);

public sealed record CreateProductImageRequest(
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int Width,
    int Height,
    string? BlurHash,
    bool IsPrimary,
    int DisplayOrder);

// ============================================================================================
// Query
// ============================================================================================

public enum ProductSort
{
    /// <summary>Curated relevance: in-stock first, then featured, then newest.</summary>
    Relevance = 0,
    Newest = 1,
    PriceLowToHigh = 2,
    PriceHighToLow = 3,
    TopRated = 4,
    BestSelling = 5,
    NameAToZ = 6
}

/// <summary>
/// Catalogue query. Page size is clamped by <see cref="PagedQuery"/>, so no request — however it
/// is constructed — can ask the server for an unbounded result set.
/// </summary>
public sealed record ProductQuery : PagedQuery
{
    public string? Search { get; init; }
    public string? CategorySlug { get; init; }
    public Guid? CategoryId { get; init; }
    public string? BrandSlug { get; init; }
    public Guid? BrandId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? InStockOnly { get; init; }
    public bool? OnSaleOnly { get; init; }
    public int? MinRating { get; init; }
    public string? Tag { get; init; }
    public ProductSort Sort { get; init; } = ProductSort.Relevance;

    /// <summary>Admin only. The storefront is always forced to Active.</summary>
    public ProductStatus? Status { get; init; }
}

/// <summary>
/// Facet counts shown beside the results, so a shopper never picks a filter that returns nothing.
/// </summary>
public sealed record ProductFacetsDto(
    IReadOnlyList<FacetItemDto> Categories,
    IReadOnlyList<FacetItemDto> Brands,
    decimal MinPrice,
    decimal MaxPrice,
    int InStockCount,
    int OnSaleCount);

public sealed record FacetItemDto(Guid Id, string Name, string Slug, int Count);

/// <summary>Results plus their facets, returned together so the grid costs one request.</summary>
public sealed record ProductSearchResultDto(
    PagedResult<ProductCardDto> Products,
    ProductFacetsDto? Facets);

/// <summary>Lightweight autocomplete row — just enough to render a suggestion.</summary>
public sealed record SearchSuggestionDto(
    string Name,
    string Slug,
    string? ThumbnailUrl,
    decimal MinPrice,
    string Type);

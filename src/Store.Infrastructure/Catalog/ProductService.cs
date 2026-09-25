using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Catalog;
using Store.Application.Common;
using Store.Domain.Catalog;
using Store.Domain.Enums;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Catalog;

public interface IProductService
{
    Task<ProductSearchResultDto> SearchAsync(ProductQuery query, bool includeFacets, CancellationToken ct = default);
    Task<Result<ProductDetailDto>> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(Guid productId, int take = 8, CancellationToken ct = default);
    Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(string term, int take = 8, CancellationToken ct = default);
    Task<PagedResult<AdminProductListItemDto>> AdminListAsync(ProductQuery query, CancellationToken ct = default);

    /// <summary>Loads a product in the shape the admin editor round-trips, drafts included.</summary>
    Task<Result<AdminProductDetailDto>> GetForEditAsync(Guid id, CancellationToken ct = default);
    Task<Result<ProductDetailDto>> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<Result<ProductDetailDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Catalogue reads and writes.
/// </summary>
/// <remarks>
/// The governing rule in this class: <b>the database returns exactly the fields the response
/// needs, and nothing else.</b> Every list query is a <c>Select</c> projection evaluated in SQL —
/// no entity is materialised, no collection is <c>Include</c>d, and the primary image and variant
/// count come back through correlated subqueries folded into the same statement. A catalogue page
/// is therefore one round trip regardless of how many products it contains.
/// <para>
/// This is the direct fix for the legacy store's defining defect: its product endpoint returned
/// all 4,207 products with every field, serialised twice, ignoring its own pagination — 11.3 MB in
/// 50 s. Here, the page size is clamped by <see cref="PagedQuery"/> before the query is built, so
/// an unbounded read is not expressible.
/// </para>
/// </remarks>
public sealed class ProductService(
    StoreDbContext db,
    ICacheService cache,
    ILogger<ProductService> logger) : IProductService
{
    // ==========================================================================================
    // Storefront search
    // ==========================================================================================

    public async Task<ProductSearchResultDto> SearchAsync(
        ProductQuery query, bool includeFacets, CancellationToken ct = default)
    {
        var filtered = await BuildFilteredQueryAsync(query, storefrontOnly: true, ct);

        var total = await filtered.CountAsync(ct);

        if (total == 0)
        {
            return new ProductSearchResultDto(
                PagedResult<ProductCardDto>.Empty(query.Page, query.PageSize),
                includeFacets ? EmptyFacets : null);
        }

        var items = await ApplySort(filtered, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ProductCardProjection)
            .ToListAsync(ct);

        // Facets are computed from the same filtered query, so the counts always agree with the
        // results. Only requested on the first page — a shopper paging deeper has already seen them.
        var facets = includeFacets ? await BuildFacetsAsync(filtered, ct) : null;

        return new ProductSearchResultDto(
            new PagedResult<ProductCardDto>(items, query.Page, query.PageSize, total),
            facets);
    }

    /// <summary>
    /// The product-card projection, defined once.
    /// </summary>
    /// <remarks>
    /// Kept as a static expression rather than repeated inline so the storefront grid, related
    /// products and merchandising rails all return an identical shape — and so the columns the
    /// covering indexes were built around stay in one place.
    /// <para>
    /// The image lookup is a correlated subquery: EF folds it into the outer statement, so it
    /// costs one indexed seek per row inside a single query rather than an extra round trip.
    /// That is the N+1 that <c>Include(p =&gt; p.Images)</c> would have caused.
    /// </para>
    /// </remarks>
    private static readonly System.Linq.Expressions.Expression<Func<Product, ProductCardDto>> ProductCardProjection =
        p => new ProductCardDto(
            p.Id,
            p.Name,
            p.Slug,
            p.ShortDescription,
            // One ordered subquery per field, not a COALESCE of two.
            //
            // The obvious shape — `Where(IsPrimary).First() ?? OrderBy(DisplayOrder).First()` —
            // compiles to COALESCE((subquery),(subquery)), doubling the subquery count for every
            // image field. Ordering by IsPrimary DESC then DisplayOrder expresses exactly the same
            // intent (prefer the primary image, otherwise the first by display order) in a single
            // subquery, halving the work.
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ThumbnailUrl).FirstOrDefault(),
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.BlurHash).FirstOrDefault(),
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Width).FirstOrDefault(),
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Height).FirstOrDefault(),
            p.MinPrice,
            p.MaxPrice,
            p.MaxCompareAtPrice,
            p.InStock,
            p.RatingAverage,
            p.RatingCount,
            p.Badge,
            p.Brand!.Name,
            p.Category!.Name,
            p.Category!.Slug,
            p.Variants.Count(v => v.IsActive),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Unit).FirstOrDefault() ?? "piece");

    private static readonly ProductFacetsDto EmptyFacets = new([], [], 0, 0, 0, 0);

    /// <summary>
    /// Builds the filtered query. Returns <see cref="IQueryable{T}"/> — deliberately not executed —
    /// so the caller can count, sort, page and facet over the same definition without repeating it.
    /// </summary>
    private async Task<IQueryable<Product>> BuildFilteredQueryAsync(
        ProductQuery query, bool storefrontOnly, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();

        if (storefrontOnly)
        {
            // Forced, never taken from the request. Otherwise a crafted query string could list
            // draft and archived products.
            products = products.Where(p => p.Status == ProductStatus.Active);
        }
        else if (query.Status is { } status)
        {
            products = products.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Two distinct searches, chosen by the shape of the term — not one combined predicate.
            //
            // Combining them (`Name.Contains(term) || Variants.Any(v => v.Sku == term)`) forces an
            // EXISTS over ProductVariants for every candidate row, which measured at ~1.08 s
            // against 4,200 products — 3.6x over the 300 ms budget. Someone typing "basmati" is
            // never searching for a SKU, and someone scanning a barcode is never doing a text
            // search, so the two cases are separated and each stays cheap.
            if (LooksLikeCode(term))
            {
                products = products.Where(p => p.Variants.Any(v => v.Sku == term || v.Barcode == term));
            }
            else
            {
                // Name only, deliberately.
                //
                // Adding `|| ShortDescription.Contains(term)` forces the supporting index to
                // INCLUDE a 500-character column, which triples its width and turns the
                // unavoidable leading-wildcard scan from ~75 ms into ~280 ms. Product names here
                // already carry the brand and the item ("Shan Basmati Rice"), so the second
                // column bought almost no extra recall for roughly 4x the cost.
                //
                // The scalable answer is a SQL Server full-text index with CONTAINS, which seeks
                // instead of scanning. That needs the Full-Text Search feature installed on the
                // server — it is NOT installed on this instance
                // (SERVERPROPERTY('IsFullTextInstalled') = 0), so it is a deployment prerequisite
                // rather than something that can be enabled from code.
                products = products.Where(p => p.Name.Contains(term));
            }
        }

        // Category browse includes descendants. Resolved through the materialised path, so
        // "Frozen" returns everything beneath it with one indexed prefix match — no recursive CTE.
        if (!string.IsNullOrWhiteSpace(query.CategorySlug) || query.CategoryId is not null)
        {
            var path = await db.Categories
                .AsNoTracking()
                .Where(c => query.CategoryId != null ? c.Id == query.CategoryId : c.Slug == query.CategorySlug)
                .Select(c => c.Path)
                .FirstOrDefaultAsync(ct);

            if (path is not null)
            {
                var descendantIds = db.Categories
                    .AsNoTracking()
                    .Where(c => c.Path.StartsWith(path))
                    .Select(c => c.Id);

                products = products.Where(p => p.CategoryId != null && descendantIds.Contains(p.CategoryId.Value));
            }
            else
            {
                // An unknown category must return nothing, not everything.
                products = products.Where(_ => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.BrandSlug))
        {
            products = products.Where(p => p.Brand!.Slug == query.BrandSlug);
        }
        else if (query.BrandId is { } brandId)
        {
            products = products.Where(p => p.BrandId == brandId);
        }

        // Filters run against the denormalised columns on Product, so price and availability
        // never require aggregating ProductVariants at read time.
        if (query.MinPrice is { } min) products = products.Where(p => p.MaxPrice >= min);
        if (query.MaxPrice is { } max) products = products.Where(p => p.MinPrice <= max);
        if (query.InStockOnly == true) products = products.Where(p => p.InStock);
        if (query.OnSaleOnly == true) products = products.Where(p => p.MaxCompareAtPrice > p.MinPrice);
        if (query.MinRating is { } rating) products = products.Where(p => p.RatingAverage >= rating);

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            products = products.Where(p => p.ProductTags.Any(pt => pt.Tag.Slug == query.Tag));
        }

        return products;
    }

    /// <summary>
    /// Decides whether a search term is a SKU or barcode rather than free text.
    /// </summary>
    /// <remarks>
    /// SKUs here look like <c>SHA-RED-1042</c> and barcodes are long digit strings; neither
    /// contains a space, and both contain digits. Product names essentially always contain a
    /// space or contain no digits at all, so this separates the two cases reliably without
    /// needing the caller to declare which kind of search it is doing.
    /// </remarks>
    private static bool LooksLikeCode(string term) =>
        !term.Contains(' ', StringComparison.Ordinal)
        && term.Any(char.IsDigit)
        && term.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Applies the sort. Each branch is backed by a matching index (see the catalogue
    /// configuration), so sorting never becomes a table scan plus in-memory ordering.
    /// </summary>
    private static IQueryable<Product> ApplySort(IQueryable<Product> products, ProductSort sort) => sort switch
    {
        ProductSort.Newest => products.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt),
        ProductSort.PriceLowToHigh => products.OrderBy(p => p.MinPrice).ThenBy(p => p.Name),
        ProductSort.PriceHighToLow => products.OrderByDescending(p => p.MinPrice).ThenBy(p => p.Name),
        ProductSort.TopRated => products.OrderByDescending(p => p.RatingAverage).ThenByDescending(p => p.RatingCount),
        ProductSort.BestSelling => products.OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.RatingAverage),
        ProductSort.NameAToZ => products.OrderBy(p => p.Name),

        // Default relevance: sellable items first (nothing is more irrelevant than an item the
        // shopper cannot buy), then curated picks, then recency.
        _ => products
            .OrderByDescending(p => p.InStock)
            .ThenByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.SalesCount)
            .ThenByDescending(p => p.CreatedAt)
    };

    /// <summary>
    /// Computes facet counts over the already-filtered query.
    /// </summary>
    /// <remarks>
    /// Four aggregate queries rather than one per facet value. They run only for the first page of
    /// a result set, which keeps the cost off deep pagination.
    /// </remarks>
    private async Task<ProductFacetsDto> BuildFacetsAsync(IQueryable<Product> filtered, CancellationToken ct)
    {
        // Grouped on the scalar foreign key, not on the navigation property.
        // Grouping by `p.Category.Name` forces EF to group across a LEFT JOIN, which it cannot
        // translate — it fails at runtime with "could not be translated". Counting by id and
        // resolving the labels afterwards keeps the aggregate a plain indexed GROUP BY, and the
        // follow-up lookup is capped at 20 rows.
        var categoryCounts = await filtered
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToListAsync(ct);

        var brandCounts = await filtered
            .Where(p => p.BrandId != null)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToListAsync(ct);

        var categoryIds = categoryCounts.Select(c => c.Id).ToList();
        var brandIds = brandCounts.Select(b => b.Id).ToList();

        var categoryLabels = await db.Categories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Slug })
            .ToDictionaryAsync(c => c.Id, ct);

        var brandLabels = await db.Brands
            .AsNoTracking()
            .Where(b => brandIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name, b.Slug })
            .ToDictionaryAsync(b => b.Id, ct);

        // Joined in memory over at most 20 rows each.
        var categories = categoryCounts
            .Where(c => categoryLabels.ContainsKey(c.Id))
            .Select(c => new FacetItemDto(c.Id, categoryLabels[c.Id].Name, categoryLabels[c.Id].Slug, c.Count))
            .ToList();

        var brands = brandCounts
            .Where(b => brandLabels.ContainsKey(b.Id))
            .Select(b => new FacetItemDto(b.Id, brandLabels[b.Id].Name, brandLabels[b.Id].Slug, b.Count))
            .ToList();

        // One round trip for all four scalars, rather than four.
        var stats = await filtered
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Min = g.Min(p => p.MinPrice),
                Max = g.Max(p => p.MaxPrice),
                InStock = g.Count(p => p.InStock),
                OnSale = g.Count(p => p.MaxCompareAtPrice > p.MinPrice)
            })
            .FirstOrDefaultAsync(ct);

        return new ProductFacetsDto(
            categories,
            brands,
            stats?.Min ?? 0,
            stats?.Max ?? 0,
            stats?.InStock ?? 0,
            stats?.OnSale ?? 0);
    }

    // ==========================================================================================
    // Product detail
    // ==========================================================================================

    public async Task<Result<ProductDetailDto>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        // The one place a fuller graph is genuinely needed. Still projected rather than loaded as
        // entities, and still a single product — bounded by definition.
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ShortDescription, p.Description, p.Badge,
                p.CategoryId, CategoryName = p.Category!.Name, CategorySlug = p.Category!.Slug,
                CategoryPath = p.Category!.Path,
                p.BrandId, BrandName = p.Brand!.Name, BrandSlug = p.Brand!.Slug,
                p.MinPrice, p.MaxPrice, p.InStock, p.TotalStock,
                p.RatingAverage, p.RatingCount, p.MetaTitle, p.MetaDescription,

                Images = p.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => new ImageDto(
                        i.Id, i.Url, i.ThumbnailUrl, i.AltText, i.Width, i.Height,
                        i.BlurHash, i.IsPrimary, i.DisplayOrder))
                    .ToList(),

                Options = p.Options
                    .OrderBy(o => o.DisplayOrder)
                    .Select(o => new ProductOptionDto(
                        o.Id, o.Name, o.DisplayOrder,
                        o.Values
                            .OrderBy(v => v.DisplayOrder)
                            .Select(v => new ProductOptionValueDto(v.Id, v.Value, v.HexColor, v.DisplayOrder))
                            .ToList()))
                    .ToList(),

                Variants = p.Variants
                    .Where(v => v.IsActive)
                    .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Price)
                    .Select(v => new ProductVariantDto(
                        v.Id, v.Name, v.Sku, v.Barcode, v.Price, v.CompareAtPrice,
                        v.StockQuantity - v.ReservedQuantity > 0 ? v.StockQuantity - v.ReservedQuantity : 0,
                        !v.TrackInventory || v.AllowBackorder || v.StockQuantity - v.ReservedQuantity > 0,
                        v.TrackInventory && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold,
                        v.Unit, v.UnitValue,
                        v.UnitValue > 0 ? v.Price / v.UnitValue : null,
                        v.WeightGrams, v.IsDefault, v.DisplayOrder,
                        v.OptionValues.Select(ov => ov.OptionValueId).ToList()))
                    .ToList(),

                Tags = p.ProductTags.Select(pt => pt.Tag.Name).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (product is null)
        {
            return Result<ProductDetailDto>.NotFound("Product not found.");
        }

        var breadcrumbs = await BuildBreadcrumbsAsync(product.CategoryPath, ct);

        return Result<ProductDetailDto>.Success(new ProductDetailDto(
            product.Id, product.Name, product.Slug, product.ShortDescription, product.Description,
            product.Badge, product.CategoryId, product.CategoryName, product.CategorySlug,
            product.BrandId, product.BrandName, product.BrandSlug,
            product.MinPrice, product.MaxPrice, product.InStock, product.TotalStock,
            product.RatingAverage, product.RatingCount, product.MetaTitle, product.MetaDescription,
            product.Images, product.Options, product.Variants, product.Tags, breadcrumbs));
    }

    /// <summary>
    /// Builds the ancestor trail from the materialised path — one query for the whole chain,
    /// rather than walking parent links one level at a time.
    /// </summary>
    private async Task<List<BreadcrumbDto>> BuildBreadcrumbsAsync(string? categoryPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(categoryPath))
        {
            return [];
        }

        var slugs = categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return await db.Categories
            .AsNoTracking()
            .Where(c => slugs.Contains(c.Slug))
            .OrderBy(c => c.Depth)
            .Select(c => new BreadcrumbDto(c.Name, c.Slug))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(
        Guid productId, int take = 8, CancellationToken ct = default)
    {
        // Clamped: this is reachable from a public URL, so the caller does not get to decide how
        // much work the server does.
        take = Math.Clamp(take, 1, 24);

        var source = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.CategoryId, p.BrandId })
            .FirstOrDefaultAsync(ct);

        if (source is null)
        {
            return [];
        }

        return await db.Products
            .AsNoTracking()
            .Where(p => p.Id != productId
                        && p.Status == ProductStatus.Active
                        && (p.CategoryId == source.CategoryId || p.BrandId == source.BrandId))
            // Same category outranks same brand; in-stock outranks both.
            .OrderByDescending(p => p.CategoryId == source.CategoryId)
            .ThenByDescending(p => p.InStock)
            .ThenByDescending(p => p.SalesCount)
            .Take(take)
            .Select(ProductCardProjection)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(
        string term, int take = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        take = Math.Clamp(take, 1, 20);
        var search = term.Trim();

        // Fires on every keystroke, so it stays deliberately tiny: four columns, capped rows.
        return await db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && p.Name.Contains(search))
            .OrderByDescending(p => p.InStock)
            .ThenByDescending(p => p.SalesCount)
            .Take(take)
            .Select(p => new SearchSuggestionDto(
                p.Name,
                p.Slug,
                p.Images.Where(i => i.IsPrimary).Select(i => i.ThumbnailUrl).FirstOrDefault(),
                p.MinPrice,
                "product"))
            .ToListAsync(ct);
    }

    // ==========================================================================================
    // Admin
    // ==========================================================================================

    public async Task<PagedResult<AdminProductListItemDto>> AdminListAsync(
        ProductQuery query, CancellationToken ct = default)
    {
        var filtered = await BuildFilteredQueryAsync(query, storefrontOnly: false, ct);
        var total = await filtered.CountAsync(ct);

        var items = await ApplySort(filtered, query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(p => new AdminProductListItemDto(
                p.Id, p.Name, p.Slug,
                p.Images.Where(i => i.IsPrimary).Select(i => i.ThumbnailUrl).FirstOrDefault(),
                p.Category!.Name, p.Brand!.Name,
                p.Status, p.MinPrice, p.MaxPrice, p.TotalStock,
                p.Variants.Count(v => v.IsActive),
                p.IsFeatured, p.IsTrending,
                p.Variants.Any(v => v.IsActive && v.TrackInventory
                                    && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold),
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminProductListItemDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// The editor's read. One query, projected in SQL like every other read in this class.
    /// </summary>
    /// <remarks>
    /// Ignores <see cref="ProductStatus"/> entirely — a draft is precisely the thing an editor
    /// most needs to open, and the storefront read filters drafts out.
    /// <para>
    /// <c>HasHistory</c> is two correlated EXISTS subqueries rather than counts: the editor only
    /// needs to know <i>whether</i> a variant can still be removed outright, and EXISTS stops at
    /// the first row where COUNT would scan every order line the variant ever appeared on.
    /// </para>
    /// </remarks>
    public async Task<Result<AdminProductDetailDto>> GetForEditAsync(
        Guid id, CancellationToken ct = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new AdminProductDetailDto(
                p.Id, p.Name, p.Slug, p.ShortDescription, p.Description,
                p.CategoryId, p.BrandId, p.Status, p.Badge,
                p.IsFeatured, p.FeaturedOrder, p.IsTrending, p.TrendingOrder, p.IsHero, p.HeroOrder,
                p.MetaTitle, p.MetaDescription, p.PublishedAt,
                p.ProductTags.Select(pt => pt.Tag.Name).ToList(),
                p.Options
                    .OrderBy(o => o.DisplayOrder)
                    .Select(o => new AdminProductOptionDto(
                        o.Id, o.Name, o.DisplayOrder,
                        o.Values
                            .OrderBy(v => v.DisplayOrder)
                            .Select(v => new AdminProductOptionValueDto(v.Id, v.Value, v.HexColor, v.DisplayOrder))
                            .ToList()))
                    .ToList(),
                p.Variants
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new AdminVariantDto(
                        v.Id, v.Name, v.Sku, v.Barcode,
                        v.Price, v.CompareAtPrice, v.CostPrice,
                        v.StockQuantity, v.ReservedQuantity, v.LowStockThreshold,
                        v.TrackInventory, v.AllowBackorder,
                        v.WeightGrams, v.Unit, v.UnitValue,
                        v.IsDefault, v.IsActive, v.DisplayOrder,
                        db.OrderItems.Any(oi => oi.ProductVariantId == v.Id)
                            || db.InventoryTransactions.Any(t => t.ProductVariantId == v.Id),
                        v.OptionValues
                            .Select(ov => new VariantOptionSelection(ov.OptionValue.Option.Name, ov.OptionValue.Value))
                            .ToList()))
                    .ToList(),
                p.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => new ImageDto(i.Id, i.Url, i.ThumbnailUrl, i.AltText,
                        i.Width, i.Height, i.BlurHash, i.IsPrimary, i.DisplayOrder))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return product is null
            ? Result<AdminProductDetailDto>.NotFound("Product not found.")
            : Result<AdminProductDetailDto>.Success(product);
    }

    public async Task<Result<ProductDetailDto>> CreateAsync(
        CreateProductRequest request, CancellationToken ct = default)
    {
        if (request.Variants.Count == 0)
        {
            return Result<ProductDetailDto>.Failure(
                "A product needs at least one variant, because price and stock live on the variant.");
        }

        var slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Products.AnyAsync(p => p.Slug == s, token),
            ct);

        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = slug,
            ShortDescription = request.ShortDescription,
            Description = request.Description,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Status = request.Status,
            Badge = request.Badge,
            IsFeatured = request.IsFeatured,
            IsTrending = request.IsTrending,
            IsHero = request.IsHero,
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            PublishedAt = request.Status == ProductStatus.Active ? DateTimeOffset.UtcNow : null
        };

        foreach (var (variant, index) in request.Variants.Select((v, i) => (v, i)))
        {
            product.Variants.Add(await BuildVariantAsync(variant, product.Name, index, ct));
        }

        // Exactly one default is guaranteed: the UI's choice if it made one, otherwise the first.
        if (!product.Variants.Any(v => v.IsDefault))
        {
            product.Variants.First().IsDefault = true;
        }

        if (request.Images is { Count: > 0 })
        {
            foreach (var (image, index) in request.Images.Select((img, i) => (img, i)))
            {
                product.Images.Add(new ProductImage
                {
                    Url = image.Url,
                    ThumbnailUrl = image.ThumbnailUrl,
                    AltText = image.AltText ?? product.Name,
                    Width = image.Width,
                    Height = image.Height,
                    BlurHash = image.BlurHash,
                    IsPrimary = image.IsPrimary || index == 0,
                    DisplayOrder = image.DisplayOrder
                });
            }
        }

        if (request.Tags is { Count: > 0 })
        {
            await AttachTagsAsync(product, request.Tags, ct);
        }

        // Aggregates are computed before the insert, so the row is never briefly wrong.
        RecalculateAggregates(product);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        await SeedInitialStockLedgerAsync(product.Variants, "Opening balance on product creation", ct);
        await InvalidateAsync(ct);

        logger.LogInformation(
            "Created product {Name} ({Slug}) with {VariantCount} variant(s)",
            product.Name, slug, product.Variants.Count);

        return await GetByIdForAdminAsync(product.Id, ct);
    }

    public async Task<Result<ProductDetailDto>> UpdateAsync(
        Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        // The editor saves the whole product, so the whole graph is loaded. A name-only update
        // sends null for options/variants/images and pays for these includes without using them,
        // which is the price of one code path instead of two.
        var product = await db.Products
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Images)
            .Include(p => p.ProductTags)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
        {
            return Result<ProductDetailDto>.NotFound("Product not found.");
        }

        if (request.Variants is { Count: 0 })
        {
            return Result<ProductDetailDto>.Failure(
                "A product needs at least one variant, because price and stock live on the variant.");
        }

        var slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Products.AnyAsync(p => p.Slug == s && p.Id != id, token),
            ct);

        var wasPublished = product.Status == ProductStatus.Active;

        product.Name = request.Name.Trim();
        product.Slug = slug;
        product.ShortDescription = request.ShortDescription;
        product.Description = request.Description;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.Status = request.Status;
        product.Badge = request.Badge;
        product.IsFeatured = request.IsFeatured;
        product.FeaturedOrder = request.FeaturedOrder;
        product.IsTrending = request.IsTrending;
        product.TrendingOrder = request.TrendingOrder;
        product.IsHero = request.IsHero;
        product.HeroOrder = request.HeroOrder;
        product.MetaTitle = request.MetaTitle;
        product.MetaDescription = request.MetaDescription;

        // Stamped on first publish only, so it remains a true first-published date.
        if (!wasPublished && request.Status == ProductStatus.Active)
        {
            product.PublishedAt = DateTimeOffset.UtcNow;
        }

        if (request.Tags is not null)
        {
            db.ProductTags.RemoveRange(product.ProductTags);
            product.ProductTags.Clear();
            await AttachTagsAsync(product, request.Tags, ct);
        }

        // Options first: a variant's option selections are resolved against them by label, so the
        // axes and their values have to exist in the graph before the variants are reconciled.
        if (request.Options is not null)
        {
            SyncOptions(product, request.Options);
        }

        List<ProductVariant> newVariants = [];

        if (request.Variants is not null)
        {
            var sync = await SyncVariantsAsync(product, request.Variants, ct);

            if (!sync.Succeeded)
            {
                return Result<ProductDetailDto>.Failure(sync.Error!);
            }

            newVariants = sync.Required;
        }

        if (request.Images is not null)
        {
            SyncImages(product, request.Images);
        }

        RecalculateAggregates(product);

        await db.SaveChangesAsync(ct);

        // Opening balances for variants added in this save. Runs after the insert because the
        // ledger rows reference variant ids, and it mirrors what CreateAsync does for a new
        // product — a variant that appears with stock must be explained by the ledger too.
        if (newVariants.Count > 0)
        {
            await SeedInitialStockLedgerAsync(newVariants, "Opening balance on variant creation", ct);
        }

        await InvalidateAsync(ct);

        return await GetByIdForAdminAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
        {
            return Result.NotFound("Product not found.");
        }

        // Soft delete: order history references this product's variants, and the global query
        // filter hides it from every storefront query from here on.
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);

        logger.LogInformation("Soft-deleted product {ProductId} ({Name})", id, product.Name);
        return Result.Success();
    }

    // ==========================================================================================
    // Internals
    // ==========================================================================================

    private async Task<ProductVariant> BuildVariantAsync(
        CreateVariantRequest request, string productName, int index, CancellationToken ct)
    {
        var sku = string.IsNullOrWhiteSpace(request.Sku)
            ? await GenerateSkuAsync(productName, index, ct)
            : request.Sku.Trim().ToUpperInvariant();

        return new ProductVariant
        {
            Name = request.Name,
            Sku = sku,
            Barcode = request.Barcode,
            Price = request.Price,
            CompareAtPrice = request.CompareAtPrice,
            CostPrice = request.CostPrice,
            StockQuantity = request.StockQuantity,
            LowStockThreshold = request.LowStockThreshold,
            TrackInventory = request.TrackInventory,
            AllowBackorder = request.AllowBackorder,
            WeightGrams = request.WeightGrams,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit,
            UnitValue = request.UnitValue,
            IsDefault = request.IsDefault,
            DisplayOrder = request.DisplayOrder == 0 ? index : request.DisplayOrder,
            IsActive = true
        };
    }

    // ------------------------------------------------------------------------------------------
    // Editor reconciliation
    //
    // All three follow the same contract: the incoming list is the complete desired state. A row
    // carrying an id is matched and updated, a row without one is created, and anything the client
    // did not send is removed. The alternative — separate add/update/delete endpoints per child
    // collection — would make an editor that changes four things in one screen issue a dozen
    // requests and have no way to roll them back together.
    // ------------------------------------------------------------------------------------------

    /// <summary>Reconciles a product's option axes and their values against the desired state.</summary>
    private void SyncOptions(Product product, IReadOnlyList<SaveProductOptionRequest> incoming)
    {
        var keptOptionIds = incoming.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();

        foreach (var option in product.Options.Where(o => !keptOptionIds.Contains(o.Id)).ToList())
        {
            // The join rows go first: VariantOptionValue → ProductOptionValue is Restrict (two
            // cascade paths into the join table is not something SQL Server will accept), so the
            // value delete fails on a foreign key unless its links are removed explicitly.
            RemoveOptionValues(product, option.Values.ToList());
            product.Options.Remove(option);
            db.ProductOptions.Remove(option);
        }

        foreach (var (request, index) in incoming.Select((o, i) => (o, i)))
        {
            var option = request.Id is { } optionId
                ? product.Options.FirstOrDefault(o => o.Id == optionId)
                : null;

            if (option is null)
            {
                option = new ProductOption { ProductId = product.Id };
                product.Options.Add(option);
            }

            option.Name = request.Name.Trim();
            option.DisplayOrder = request.DisplayOrder == 0 ? index : request.DisplayOrder;

            var keptValueIds = request.Values.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();

            RemoveOptionValues(product, option.Values.Where(v => !keptValueIds.Contains(v.Id)).ToList(), option);

            foreach (var (valueRequest, valueIndex) in request.Values.Select((v, i) => (v, i)))
            {
                var value = valueRequest.Id is { } valueId
                    ? option.Values.FirstOrDefault(v => v.Id == valueId)
                    : null;

                if (value is null)
                {
                    value = new ProductOptionValue { OptionId = option.Id };
                    option.Values.Add(value);
                }

                value.Value = valueRequest.Value.Trim();
                value.HexColor = valueRequest.HexColor;
                value.DisplayOrder = valueRequest.DisplayOrder == 0 ? valueIndex : valueRequest.DisplayOrder;
            }
        }
    }

    private void RemoveOptionValues(
        Product product, List<ProductOptionValue> values, ProductOption? owner = null)
    {
        if (values.Count == 0)
        {
            return;
        }

        var valueIds = values.Select(v => v.Id).ToHashSet();

        // Only this product's variants can link to this product's option values, and all of them
        // are loaded, so walking the graph finds every link without another round trip.
        foreach (var variant in product.Variants)
        {
            foreach (var link in variant.OptionValues.Where(l => valueIds.Contains(l.OptionValueId)).ToList())
            {
                variant.OptionValues.Remove(link);
                db.VariantOptionValues.Remove(link);
            }
        }

        foreach (var value in values)
        {
            owner?.Values.Remove(value);
            db.ProductOptionValues.Remove(value);
        }
    }

    /// <summary>
    /// Reconciles variants. Returns the ones created, so the caller can seed their stock ledger.
    /// </summary>
    /// <remarks>
    /// A variant the client dropped is <b>deactivated rather than deleted</b> once it has any
    /// history. Order lines and the stock ledger both point at it, and a hard delete would either
    /// fail on a foreign key or quietly take a customer's order history with it. Deactivating
    /// removes it from the storefront, which is what the admin actually meant.
    /// </remarks>
    private async Task<Result<List<ProductVariant>>> SyncVariantsAsync(
        Product product, IReadOnlyList<SaveVariantRequest> incoming, CancellationToken ct)
    {
        var keptIds = incoming.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();
        var dropped = product.Variants.Where(v => !keptIds.Contains(v.Id)).ToList();

        if (dropped.Count > 0)
        {
            var droppedIds = dropped.Select(v => v.Id).ToList();

            var withHistory = await db.OrderItems
                .Where(oi => oi.ProductVariantId != null && droppedIds.Contains(oi.ProductVariantId!.Value))
                .Select(oi => oi.ProductVariantId!.Value)
                .Union(db.InventoryTransactions
                    .Where(t => droppedIds.Contains(t.ProductVariantId))
                    .Select(t => t.ProductVariantId))
                .ToListAsync(ct);

            var historical = withHistory.ToHashSet();

            foreach (var variant in dropped)
            {
                if (historical.Contains(variant.Id))
                {
                    variant.IsActive = false;
                    variant.IsDefault = false;
                }
                else
                {
                    db.VariantOptionValues.RemoveRange(variant.OptionValues);
                    product.Variants.Remove(variant);
                    db.ProductVariants.Remove(variant);
                }
            }
        }

        // Option values are addressed by label, so build the lookup once rather than per variant.
        var valuesByLabel = product.Options
            .SelectMany(o => o.Values.Select(v => (Option: o.Name, v.Value, Id: v.Id)))
            .ToDictionary(x => OptionKey(x.Option, x.Value), x => x.Id);

        var created = new List<ProductVariant>();

        foreach (var (request, index) in incoming.Select((v, i) => (v, i)))
        {
            var variant = request.Id is { } variantId
                ? product.Variants.FirstOrDefault(v => v.Id == variantId)
                : null;

            if (variant is null)
            {
                variant = await BuildVariantAsync(
                    new CreateVariantRequest(
                        request.Name, request.Sku, request.Barcode, request.Price,
                        request.CompareAtPrice, request.CostPrice, request.StockQuantity,
                        request.LowStockThreshold, request.TrackInventory, request.AllowBackorder,
                        request.WeightGrams, request.Unit, request.UnitValue,
                        request.IsDefault, request.DisplayOrder),
                    product.Name, index, ct);

                variant.ProductId = product.Id;
                product.Variants.Add(variant);
                created.Add(variant);
            }
            else
            {
                variant.Name = request.Name;
                variant.Barcode = request.Barcode;
                variant.Price = request.Price;
                variant.CompareAtPrice = request.CompareAtPrice;
                variant.CostPrice = request.CostPrice;
                variant.LowStockThreshold = request.LowStockThreshold;
                variant.TrackInventory = request.TrackInventory;
                variant.AllowBackorder = request.AllowBackorder;
                variant.WeightGrams = request.WeightGrams;
                variant.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit;
                variant.UnitValue = request.UnitValue;
                variant.IsActive = request.IsActive;
                variant.IsDefault = request.IsDefault;
                variant.DisplayOrder = request.DisplayOrder == 0 ? index : request.DisplayOrder;

                // StockQuantity is deliberately not assigned here. Stock moves through the
                // inventory endpoints, which write a ledger row saying who moved it and why; a
                // product form that set it directly would leave the ledger unable to account for
                // the difference. An editable SKU is refused for the same reason — it is printed
                // on shelf labels and quoted on past orders.
                if (!string.IsNullOrWhiteSpace(request.Sku))
                {
                    var sku = request.Sku.Trim().ToUpperInvariant();

                    if (!string.Equals(sku, variant.Sku, StringComparison.Ordinal))
                    {
                        if (await db.ProductVariants.AnyAsync(v => v.Sku == sku && v.Id != variant.Id, ct))
                        {
                            return Result<List<ProductVariant>>.Failure($"The SKU {sku} is already in use.");
                        }

                        variant.Sku = sku;
                    }
                }
            }

            SyncVariantOptionValues(variant, request.OptionValues, valuesByLabel);
        }

        var active = product.Variants.Where(v => v.IsActive).ToList();

        if (active.Count == 0)
        {
            return Result<List<ProductVariant>>.Failure(
                "At least one variant must stay active, otherwise the product has no price and cannot be sold.");
        }

        // Exactly one default, always. More than one and the product page picks arbitrarily;
        // none and it has nothing to preselect.
        var defaults = active.Where(v => v.IsDefault).ToList();

        if (defaults.Count != 1)
        {
            foreach (var variant in product.Variants)
            {
                variant.IsDefault = false;
            }

            (defaults.FirstOrDefault() ?? active[0]).IsDefault = true;
        }

        return Result<List<ProductVariant>>.Success(created);
    }

    private void SyncVariantOptionValues(
        ProductVariant variant,
        IReadOnlyList<VariantOptionSelection>? selections,
        Dictionary<string, Guid> valuesByLabel)
    {
        if (selections is null)
        {
            return;
        }

        var desired = selections
            .Select(s => valuesByLabel.TryGetValue(OptionKey(s.Option, s.Value), out var valueId) ? valueId : (Guid?)null)
            .Where(valueId => valueId is not null)
            .Select(valueId => valueId!.Value)
            .ToHashSet();

        foreach (var link in variant.OptionValues.Where(l => !desired.Contains(l.OptionValueId)).ToList())
        {
            variant.OptionValues.Remove(link);
            db.VariantOptionValues.Remove(link);
        }

        foreach (var valueId in desired.Where(valueId => variant.OptionValues.All(l => l.OptionValueId != valueId)))
        {
            variant.OptionValues.Add(new VariantOptionValue
            {
                VariantId = variant.Id,
                OptionValueId = valueId
            });
        }
    }

    /// <summary>Case- and whitespace-insensitive key for matching an option value by label.</summary>
    private static string OptionKey(string option, string value) =>
        $"{option.Trim().ToLowerInvariant()}\u0000{value.Trim().ToLowerInvariant()}";

    /// <summary>Reconciles the image set, guaranteeing exactly one primary.</summary>
    private void SyncImages(Product product, IReadOnlyList<SaveProductImageRequest> incoming)
    {
        var keptIds = incoming.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToHashSet();

        foreach (var image in product.Images.Where(i => !keptIds.Contains(i.Id)).ToList())
        {
            product.Images.Remove(image);
            db.ProductImages.Remove(image);
        }

        // The entity each request maps to is recorded as we go. Matching them back up afterwards
        // by id would miss a freshly added image, whose request carries no id — which is exactly
        // the case where someone uploads a photo and ticks it as the primary one.
        ProductImage? primary = null;
        ProductImage? first = null;

        foreach (var (request, index) in incoming.Select((img, i) => (img, i)))
        {
            var image = request.Id is { } imageId
                ? product.Images.FirstOrDefault(i => i.Id == imageId)
                : null;

            if (image is null)
            {
                image = new ProductImage { ProductId = product.Id };
                product.Images.Add(image);
            }

            image.Url = request.Url;
            image.ThumbnailUrl = request.ThumbnailUrl;
            image.AltText = string.IsNullOrWhiteSpace(request.AltText) ? product.Name : request.AltText;
            image.Width = request.Width;
            image.Height = request.Height;
            image.BlurHash = request.BlurHash;
            image.DisplayOrder = request.DisplayOrder == 0 ? index : request.DisplayOrder;
            image.IsPrimary = false;

            first ??= image;

            // First wins, so a payload that marks two primaries resolves rather than being rejected.
            if (request.IsPrimary)
            {
                primary ??= image;
            }
        }

        // Falls back to the first image, so a set with no primary still has one.
        var chosen = primary ?? first;

        if (chosen is not null)
        {
            chosen.IsPrimary = true;
        }
    }

    /// <summary>
    /// Derives a readable, unique SKU from the product name — <c>BAS-RIC-5KG-0001</c>.
    /// </summary>
    /// <remarks>
    /// Readable on purpose: staff read SKUs aloud and type them into a POS. The legacy store's
    /// identifiers were pipe-delimited Firebase composites
    /// (<c>wq5B3gH54fMCxahE7Bwg|xJvKgffOX3fpR9z1koGM</c>), which are unusable by a human.
    /// </remarks>
    private async Task<string> GenerateSkuAsync(string productName, int index, CancellationToken ct)
    {
        var prefix = string.Concat(productName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant()));

        if (prefix.Length == 0)
        {
            prefix = "SKU";
        }

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"{prefix}-{Random.Shared.Next(1000, 9999)}{(index > 0 ? $"-{index + 1}" : string.Empty)}";

            if (!await db.ProductVariants.AnyAsync(v => v.Sku == candidate, ct))
            {
                return candidate;
            }
        }

        return $"{prefix}-{Guid.CreateVersion7().ToString("N")[..8].ToUpperInvariant()}";
    }

    /// <summary>
    /// Recomputes the denormalised columns on <see cref="Product"/> from its variants.
    /// </summary>
    /// <remarks>
    /// Called inside the same transaction as any write that changes variant price or stock, so the
    /// catalogue's sort and filter columns can never drift from the variants they summarise. This
    /// is the trade that makes the grid fast: a little work on write, none on read.
    /// </remarks>
    internal static void RecalculateAggregates(Product product)
    {
        var active = product.Variants.Where(v => v.IsActive).ToList();

        if (active.Count == 0)
        {
            product.MinPrice = 0;
            product.MaxPrice = 0;
            product.MaxCompareAtPrice = null;
            product.InStock = false;
            product.TotalStock = 0;
            return;
        }

        product.MinPrice = active.Min(v => v.Price);
        product.MaxPrice = active.Max(v => v.Price);

        var compareAt = active.Where(v => v.CompareAtPrice > v.Price).Select(v => v.CompareAtPrice!.Value).ToList();
        product.MaxCompareAtPrice = compareAt.Count > 0 ? compareAt.Max() : null;

        product.TotalStock = active.Sum(v => Math.Max(0, v.StockQuantity - v.ReservedQuantity));
        product.InStock = active.Any(v => !v.TrackInventory || v.AllowBackorder || v.StockQuantity - v.ReservedQuantity > 0);
    }

    /// <summary>Records the opening balance so the stock ledger explains every unit from day one.</summary>
    private async Task SeedInitialStockLedgerAsync(
        IEnumerable<ProductVariant> variants, string note, CancellationToken ct)
    {
        var openings = variants
            .Where(v => v.StockQuantity != 0)
            .Select(v => new Domain.Inventory.InventoryTransaction
            {
                ProductVariantId = v.Id,
                Type = InventoryTransactionType.Initial,
                QuantityChange = v.StockQuantity,
                QuantityAfter = v.StockQuantity,
                Note = note
            })
            .ToList();

        if (openings.Count == 0)
        {
            return;
        }

        db.InventoryTransactions.AddRange(openings);
        await db.SaveChangesAsync(ct);
    }

    private async Task AttachTagsAsync(Product product, IReadOnlyList<string> tagNames, CancellationToken ct)
    {
        foreach (var name in tagNames.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var slug = SlugGenerator.Generate(name);
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);

            if (tag is null)
            {
                tag = new Tag { Name = name, Slug = slug };
                db.Tags.Add(tag);
            }

            product.ProductTags.Add(new ProductTag { Product = product, Tag = tag });
        }
    }

    /// <summary>Admin variant of the detail read — unlike the storefront one, it ignores status.</summary>
    private async Task<Result<ProductDetailDto>> GetByIdForAdminAsync(Guid id, CancellationToken ct)
    {
        var slug = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(ct);

        if (slug is null)
        {
            return Result<ProductDetailDto>.NotFound("Product not found.");
        }

        var result = await GetBySlugAsync(slug, ct);

        // A draft is invisible to the storefront read, so fall back to a status-agnostic load.
        return result.Succeeded ? result : await GetDraftDetailAsync(id, ct);
    }

    private async Task<Result<ProductDetailDto>> GetDraftDetailAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDetailDto(
                p.Id, p.Name, p.Slug, p.ShortDescription, p.Description, p.Badge,
                p.CategoryId, p.Category!.Name, p.Category!.Slug,
                p.BrandId, p.Brand!.Name, p.Brand!.Slug,
                p.MinPrice, p.MaxPrice, p.InStock, p.TotalStock,
                p.RatingAverage, p.RatingCount, p.MetaTitle, p.MetaDescription,
                p.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => new ImageDto(i.Id, i.Url, i.ThumbnailUrl, i.AltText,
                        i.Width, i.Height, i.BlurHash, i.IsPrimary, i.DisplayOrder))
                    .ToList(),
                p.Options
                    .OrderBy(o => o.DisplayOrder)
                    .Select(o => new ProductOptionDto(o.Id, o.Name, o.DisplayOrder,
                        o.Values.OrderBy(v => v.DisplayOrder)
                            .Select(v => new ProductOptionValueDto(v.Id, v.Value, v.HexColor, v.DisplayOrder))
                            .ToList()))
                    .ToList(),
                p.Variants
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new ProductVariantDto(
                        v.Id, v.Name, v.Sku, v.Barcode, v.Price, v.CompareAtPrice,
                        v.StockQuantity - v.ReservedQuantity > 0 ? v.StockQuantity - v.ReservedQuantity : 0,
                        !v.TrackInventory || v.AllowBackorder || v.StockQuantity - v.ReservedQuantity > 0,
                        v.TrackInventory && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold,
                        v.Unit, v.UnitValue,
                        v.UnitValue > 0 ? v.Price / v.UnitValue : null,
                        v.WeightGrams, v.IsDefault, v.DisplayOrder,
                        v.OptionValues.Select(ov => ov.OptionValueId).ToList()))
                    .ToList(),
                p.ProductTags.Select(pt => pt.Tag.Name).ToList(),
                new List<BreadcrumbDto>()))
            .FirstOrDefaultAsync(ct);

        return product is null
            ? Result<ProductDetailDto>.NotFound("Product not found.")
            : Result<ProductDetailDto>.Success(product);
    }

    private Task InvalidateAsync(CancellationToken ct) =>
        cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);
}

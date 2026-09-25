using Microsoft.AspNetCore.Mvc;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Catalog;
using Store.Domain.Enums;
using Store.Domain.Identity;
using Store.Infrastructure.Catalog;

namespace Store.Api.Endpoints;

/// <summary>
/// Public catalogue browsing and admin catalogue management.
/// </summary>
/// <remarks>
/// Public reads are anonymous and output-cached; writes are gated on
/// <see cref="Permissions"/> constants. Query parameters are declared explicitly rather than
/// through <c>[AsParameters]</c>, which the minimal-API binder treats as a body parameter for a
/// complex type with inherited init-only properties — and which therefore fails on a GET.
/// </remarks>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        MapPublicCatalog(app);
        MapAdminCategories(app);
        MapAdminBrands(app);
        MapAdminProducts(app);

        return app;
    }

    // ==========================================================================================
    // Public storefront
    // ==========================================================================================

    private static void MapPublicCatalog(IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/api/catalog").WithTags("Catalogue").AllowAnonymous();

        catalog.MapGet("/categories", async (ICategoryService service, CancellationToken ct) =>
                Results.Ok(await service.GetTreeAsync(ct)))
            .CacheOutput("reference")
            .WithSummary("The category tree for navigation.");

        catalog.MapGet("/categories/{slug}", async (string slug, ICategoryService service, CancellationToken ct) =>
                (await service.GetBySlugAsync(slug, ct)).ToHttpResult())
            .CacheOutput("reference");

        catalog.MapGet("/brands", async (IBrandService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(false, ct)))
            .CacheOutput("reference");

        catalog.MapGet("/products", async (
                    IProductService service,
                    CancellationToken ct,
                    string? search = null,
                    string? category = null,
                    string? brand = null,
                    decimal? minPrice = null,
                    decimal? maxPrice = null,
                    bool? inStock = null,
                    bool? onSale = null,
                    int? minRating = null,
                    string? tag = null,
                    ProductSort sort = ProductSort.Relevance,
                    int page = 1,
                    int pageSize = 24,
                    // Facets cost four aggregates, so they are opt-in and only worth requesting
                    // on the first page — a shopper paging deeper already has them.
                    bool facets = false) =>
                Results.Ok(await service.SearchAsync(
                    new ProductQuery
                    {
                        Search = search,
                        CategorySlug = category,
                        BrandSlug = brand,
                        MinPrice = minPrice,
                        MaxPrice = maxPrice,
                        InStockOnly = inStock,
                        OnSaleOnly = onSale,
                        MinRating = minRating,
                        Tag = tag,
                        Sort = sort,
                        Page = page,
                        PageSize = pageSize
                    },
                    includeFacets: facets && page == 1,
                    ct)))
            .CacheOutput("catalog")
            .WithSummary("Search, filter and sort the catalogue. Always paged.");

        catalog.MapGet("/products/{slug}", async (string slug, IProductService service, CancellationToken ct) =>
                (await service.GetBySlugAsync(slug, ct)).ToHttpResult())
            .CacheOutput("catalog")
            .WithSummary("Full product detail, including variants and options.");

        catalog.MapGet("/products/{id:guid}/related", async (
                    Guid id, IProductService service, CancellationToken ct, int take = 8) =>
                Results.Ok(await service.GetRelatedAsync(id, take, ct)))
            .CacheOutput("catalog");

        catalog.MapGet("/suggest", async (
                    IProductService service, CancellationToken ct, string q = "", int take = 8) =>
                Results.Ok(await service.SuggestAsync(q, take, ct)))
            .WithSummary("Autocomplete suggestions. Deliberately tiny — fires on every keystroke.");
    }

    // ==========================================================================================
    // Admin — categories
    // ==========================================================================================

    private static void MapAdminCategories(IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/admin/categories").WithTags("Admin · Categories");

        categories.MapGet("/", async (ICategoryService service, CancellationToken ct, bool includeInactive = true) =>
                Results.Ok(await service.ListAsync(includeInactive, ct)))
            .RequirePermission(Permissions.Categories.View);

        categories.MapGet("/{id:guid}", async (Guid id, ICategoryService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Categories.View);

        categories.MapPost("/", async ([FromBody] CreateCategoryRequest request, ICategoryService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(c => $"/api/admin/categories/{c.Id}"))
            .RequirePermission(Permissions.Categories.Create);

        categories.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateCategoryRequest request, ICategoryService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Categories.Update);

        categories.MapDelete("/{id:guid}", async (Guid id, ICategoryService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Categories.Delete);

        categories.MapPost("/refresh-counts", async (ICategoryService service, CancellationToken ct) =>
            {
                await service.RefreshProductCountsAsync(ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.Categories.Update)
            .WithSummary("Recompute the maintained product counts across the tree.");
    }

    // ==========================================================================================
    // Admin — brands
    // ==========================================================================================

    private static void MapAdminBrands(IEndpointRouteBuilder app)
    {
        var brands = app.MapGroup("/api/admin/brands").WithTags("Admin · Brands");

        brands.MapGet("/", async (IBrandService service, CancellationToken ct, bool includeInactive = true) =>
                Results.Ok(await service.ListAsync(includeInactive, ct)))
            .RequirePermission(Permissions.Brands.View);

        brands.MapGet("/{id:guid}", async (Guid id, IBrandService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Brands.View);

        brands.MapPost("/", async ([FromBody] CreateBrandRequest request, IBrandService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(b => $"/api/admin/brands/{b.Id}"))
            .RequirePermission(Permissions.Brands.Create);

        brands.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateBrandRequest request, IBrandService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Brands.Update);

        brands.MapDelete("/{id:guid}", async (Guid id, IBrandService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Brands.Delete);
    }

    // ==========================================================================================
    // Admin — products
    // ==========================================================================================

    private static void MapAdminProducts(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/admin/products").WithTags("Admin · Products");

        products.MapGet("/", async (
                    IProductService service,
                    CancellationToken ct,
                    string? search = null,
                    Guid? categoryId = null,
                    Guid? brandId = null,
                    ProductStatus? status = null,
                    bool? inStock = null,
                    ProductSort sort = ProductSort.Newest,
                    int page = 1,
                    int pageSize = 24) =>
                Results.Ok(await service.AdminListAsync(
                    new ProductQuery
                    {
                        Search = search,
                        CategoryId = categoryId,
                        BrandId = brandId,
                        Status = status,
                        InStockOnly = inStock,
                        Sort = sort,
                        Page = page,
                        PageSize = pageSize
                    }, ct)))
            .RequirePermission(Permissions.Products.View);

        products.MapPost("/", async ([FromBody] CreateProductRequest request, IProductService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(p => $"/api/admin/products/{p.Id}"))
            .RequirePermission(Permissions.Products.Create);

        products.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateProductRequest request, IProductService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Products.Update);

        products.MapDelete("/{id:guid}", async (Guid id, IProductService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Products.Delete);
    }
}

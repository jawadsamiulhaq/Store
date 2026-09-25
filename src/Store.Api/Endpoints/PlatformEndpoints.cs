using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Domain.Identity;
using Store.Infrastructure.Commerce;
using Store.Infrastructure.Platform;

namespace Store.Api.Endpoints;

/// <summary>Coupons, customers, addresses, settings, content, media and the audit log.</summary>
public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        MapStorefrontBootstrap(app);
        MapAddresses(app);
        MapCoupons(app);
        MapCustomers(app);
        MapSettings(app);
        MapContent(app);
        MapMedia(app);
        MapAudit(app);

        return app;
    }

    // ==========================================================================================
    // Storefront bootstrap
    // ==========================================================================================

    private static void MapStorefrontBootstrap(IEndpointRouteBuilder app)
    {
        // One aggregated call rather than three separate ones. Fewer requests is part of the
        // performance budget, not just a convenience — each round trip costs a connection and a
        // render-blocking wait on a cold page load.
        app.MapGet("/api/storefront/bootstrap", async (
                    ISettingsService settings,
                    Store.Infrastructure.Catalog.ICategoryService categories,
                    IContentService content,
                    CancellationToken ct) =>
            {
                // Awaited one after another, NOT with Task.WhenAll.
                //
                // All three services share the request's scoped DbContext, and a DbContext cannot
                // serve two operations at once — running them concurrently throws
                // "A second operation was started on this context instance". It is an easy bug to
                // miss because two of the three are cache-backed: once warm only one actually
                // touches the database, so the fault only appears on a cold cache, which is
                // exactly when the first visitor arrives.
                //
                // Sequential costs nothing real here: the two cached reads return immediately, and
                // parallelism across a single connection was never going to help anyway.
                var publicSettings = await settings.GetPublicAsync(ct);
                var categoryTree = await categories.GetTreeAsync(ct);
                var footerPages = await content.GetFooterPagesAsync(ct);

                return Results.Ok(new
                {
                    settings = publicSettings,
                    categories = categoryTree,
                    footerPages
                });
            })
            .WithTags("Storefront")
            .AllowAnonymous()
            .CacheOutput("reference")
            .WithSummary("Everything the storefront shell needs, in one request.");

        app.MapGet("/api/storefront/banners", async (
                    IContentService content,
                    CancellationToken ct,
                    BannerPosition position = BannerPosition.HomeHero) =>
                Results.Ok(await content.GetActiveBannersAsync(position, ct)))
            .WithTags("Storefront")
            .AllowAnonymous()
            .CacheOutput("reference");

        app.MapGet("/api/storefront/pages/{slug}", async (
                    string slug, IContentService content, CancellationToken ct) =>
                (await content.GetPageBySlugAsync(slug, ct)).ToHttpResult())
            .WithTags("Storefront")
            .AllowAnonymous()
            .CacheOutput("reference");
    }

    // ==========================================================================================
    // Customer addresses
    // ==========================================================================================

    private static void MapAddresses(IEndpointRouteBuilder app)
    {
        var addresses = app.MapGroup("/api/addresses").WithTags("Addresses").RequireAuthorization();

        addresses.MapGet("/", async (IAddressService service, CancellationToken ct) =>
            Results.Ok(await service.GetMineAsync(ct)));

        addresses.MapPost("/", async ([FromBody] SaveAddressRequest request, IAddressService service, CancellationToken ct) =>
            (await service.SaveAsync(null, request, ct)).ToHttpResult());

        addresses.MapPut("/{id:guid}", async (Guid id, [FromBody] SaveAddressRequest request, IAddressService service, CancellationToken ct) =>
            (await service.SaveAsync(id, request, ct)).ToHttpResult());

        addresses.MapPost("/{id:guid}/default", async (Guid id, IAddressService service, CancellationToken ct) =>
            (await service.SetDefaultAsync(id, ct)).ToHttpResult());

        addresses.MapDelete("/{id:guid}", async (Guid id, IAddressService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, ct)).ToHttpResult());
    }

    // ==========================================================================================
    // Coupons
    // ==========================================================================================

    private static void MapCoupons(IEndpointRouteBuilder app)
    {
        var coupons = app.MapGroup("/api/admin/coupons").WithTags("Admin · Coupons");

        coupons.MapGet("/", async (
                    ICouponAdminService service, CancellationToken ct,
                    string? search = null, bool? isActive = null, int page = 1, int pageSize = 24) =>
                Results.Ok(await service.ListAsync(new CouponQuery
                {
                    Search = search, IsActive = isActive, Page = page, PageSize = pageSize
                }, ct)))
            .RequirePermission(Permissions.Coupons.View);

        coupons.MapGet("/{id:guid}", async (Guid id, ICouponAdminService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Coupons.View);

        coupons.MapPost("/", async ([FromBody] SaveCouponRequest request, ICouponAdminService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreatedResult(c => $"/api/admin/coupons/{c.Id}"))
            .RequirePermission(Permissions.Coupons.Create);

        coupons.MapPut("/{id:guid}", async (Guid id, [FromBody] SaveCouponRequest request, ICouponAdminService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Coupons.Update);

        coupons.MapDelete("/{id:guid}", async (Guid id, ICouponAdminService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Coupons.Delete);
    }

    // ==========================================================================================
    // Customers
    // ==========================================================================================

    private static void MapCustomers(IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/admin/customers").WithTags("Admin · Customers");

        customers.MapGet("/", async (
                    ICustomerAdminService service, CancellationToken ct,
                    string? search = null, bool? isActive = null, string? sortBy = null,
                    bool descending = true, int page = 1, int pageSize = 24) =>
                Results.Ok(await service.ListAsync(new CustomerQuery
                {
                    Search = search, IsActive = isActive, SortBy = sortBy,
                    Descending = descending, Page = page, PageSize = pageSize
                }, ct)))
            .RequirePermission(Permissions.Customers.View);

        customers.MapGet("/{id:guid}", async (Guid id, ICustomerAdminService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Customers.View);

        customers.MapPut("/{id:guid}/notes", async (Guid id, [FromBody] UpdateNotesRequest request, ICustomerAdminService service, CancellationToken ct) =>
                (await service.UpdateNotesAsync(id, request.Notes, ct)).ToHttpResult())
            .RequirePermission(Permissions.Customers.Update);
    }

    public sealed record UpdateNotesRequest(string? Notes);

    // ==========================================================================================
    // Settings
    // ==========================================================================================

    private static void MapSettings(IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/admin/settings").WithTags("Admin · Settings");

        settings.MapGet("/", async (ISettingsService service, CancellationToken ct) =>
                Results.Ok(await service.GetAllAsync(ct)))
            .RequirePermission(Permissions.Settings.View);

        settings.MapPut("/", async ([FromBody] Dictionary<string, string?> values, ISettingsService service, CancellationToken ct) =>
                (await service.UpdateAsync(values, ct)).ToHttpResult())
            .RequirePermission(Permissions.Settings.Manage);
    }

    // ==========================================================================================
    // Content
    // ==========================================================================================

    private static void MapContent(IEndpointRouteBuilder app)
    {
        var content = app.MapGroup("/api/admin/content").WithTags("Admin · Content");

        content.MapGet("/banners", async (IContentService service, CancellationToken ct) =>
                Results.Ok(await service.GetAllBannersAsync(ct)))
            .RequirePermission(Permissions.Content.View);

        content.MapPost("/banners", async ([FromBody] BannerDto request, IContentService service, CancellationToken ct) =>
                (await service.SaveBannerAsync(null, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);

        content.MapPut("/banners/{id:guid}", async (Guid id, [FromBody] BannerDto request, IContentService service, CancellationToken ct) =>
                (await service.SaveBannerAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);

        content.MapDelete("/banners/{id:guid}", async (Guid id, IContentService service, CancellationToken ct) =>
                (await service.DeleteBannerAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);

        content.MapGet("/pages", async (IContentService service, CancellationToken ct) =>
                Results.Ok(await service.GetAllPagesAsync(ct)))
            .RequirePermission(Permissions.Content.View);

        content.MapPost("/pages", async ([FromBody] ContentPageDto request, IContentService service, CancellationToken ct) =>
                (await service.SavePageAsync(null, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);

        content.MapPut("/pages/{id:guid}", async (Guid id, [FromBody] ContentPageDto request, IContentService service, CancellationToken ct) =>
                (await service.SavePageAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);

        content.MapDelete("/pages/{id:guid}", async (Guid id, IContentService service, CancellationToken ct) =>
                (await service.DeletePageAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Content.Manage);
    }

    // ==========================================================================================
    // Media
    // ==========================================================================================

    private static void MapMedia(IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/api/admin/media").WithTags("Admin · Media");

        media.MapPost("/upload", async (
                    IFormFile file,
                    IFileStorage storage,
                    Store.Infrastructure.Persistence.StoreDbContext db,
                    CancellationToken ct,
                    string? folder = null) =>
            {
                if (file.Length == 0)
                {
                    return Results.Problem(
                        title: "Invalid request", detail: "The uploaded file is empty.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                await using var stream = file.OpenReadStream();
                var stored = await storage.SaveAsync(stream, file.FileName, file.ContentType, folder, ct);

                // Recorded in the media library so the asset can be reused, and so dimensions and
                // the blur placeholder are available to every consumer that references it.
                var asset = new Domain.Content.MediaAsset
                {
                    FileName = stored.FileName,
                    OriginalFileName = file.FileName,
                    Url = stored.Url,
                    ThumbnailUrl = stored.ThumbnailUrl,
                    WebpUrl = stored.WebpUrl,
                    AvifUrl = stored.AvifUrl,
                    ContentType = file.ContentType,
                    SizeBytes = stored.SizeBytes,
                    Width = stored.Width,
                    Height = stored.Height,
                    BlurHash = stored.BlurHash,
                    Folder = folder
                };

                db.MediaAssets.Add(asset);
                await db.SaveChangesAsync(ct);

                return Results.Ok(asset);
            })
            .RequirePermission(Permissions.Media.Upload)
            .DisableAntiforgery()
            .WithSummary("Upload an image. Produces a WebP derivative, a thumbnail and a blur placeholder.");

        media.MapGet("/", async (
                    Store.Infrastructure.Persistence.StoreDbContext db,
                    CancellationToken ct,
                    string? folder = null, int page = 1, int pageSize = 40) =>
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                var assets = db.MediaAssets.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(folder))
                {
                    assets = assets.Where(a => a.Folder == folder);
                }

                var total = await assets.CountAsync(ct);

                var items = await assets
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(ct);

                return Results.Ok(new PagedResult<Domain.Content.MediaAsset>(items, page, pageSize, total));
            })
            .RequirePermission(Permissions.Media.View);

        media.MapDelete("/{id:guid}", async (
                    Guid id,
                    IFileStorage storage,
                    Store.Infrastructure.Persistence.StoreDbContext db,
                    CancellationToken ct) =>
            {
                var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, ct);

                if (asset is null)
                {
                    return Results.NotFound();
                }

                // Blocked while anything still points at it, so deleting from the library cannot
                // silently break a product page.
                var inUse = await db.ProductImages.AnyAsync(i => i.Url == asset.Url, ct)
                            || await db.Banners.AnyAsync(b => b.ImageUrl == asset.Url, ct);

                if (inUse)
                {
                    return Results.Problem(
                        title: "Conflict",
                        detail: "This image is still used by a product or banner.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                await storage.DeleteAsync(asset.Url, ct);
                db.MediaAssets.Remove(asset);
                await db.SaveChangesAsync(ct);

                return Results.NoContent();
            })
            .RequirePermission(Permissions.Media.Delete);
    }

    // ==========================================================================================
    // Audit log
    // ==========================================================================================

    private static void MapAudit(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/audit", async (
                    IAuditService service, CancellationToken ct,
                    string? action = null, string? entityType = null, Guid? userId = null,
                    DateTimeOffset? from = null, DateTimeOffset? to = null,
                    int page = 1, int pageSize = 50) =>
                Results.Ok(await service.ListAsync(new AuditQuery
                {
                    Action = action, EntityType = entityType, UserId = userId,
                    From = from, To = to, Page = page, PageSize = pageSize
                }, ct)))
            .WithTags("Admin · Audit")
            .RequirePermission(Permissions.Audit.View)
            .WithSummary("Append-only record of privileged actions.");
    }
}

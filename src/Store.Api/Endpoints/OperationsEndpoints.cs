using Microsoft.AspNetCore.Mvc;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Reviews;
using Store.Domain.Enums;
using Store.Domain.Identity;
using Store.Infrastructure.Inventory;
using Store.Infrastructure.Reporting;
using Store.Infrastructure.Reviews;

namespace Store.Api.Endpoints;

/// <summary>Reviews, inventory and reporting.</summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        MapReviews(app);
        MapInventory(app);
        MapReporting(app);

        return app;
    }

    // ==========================================================================================
    // Reviews
    // ==========================================================================================

    private static void MapReviews(IEndpointRouteBuilder app)
    {
        var reviews = app.MapGroup("/api/reviews").WithTags("Reviews");

        reviews.MapGet("/product/{productId:guid}", async (
                    Guid productId,
                    IReviewService service,
                    CancellationToken ct,
                    int page = 1,
                    int pageSize = 10,
                    ReviewSort sort = ReviewSort.MostHelpful) =>
                Results.Ok(await service.GetForProductAsync(productId, page, pageSize, sort, ct)))
            .AllowAnonymous()
            .WithSummary("Approved reviews plus the rating histogram and the caller's eligibility.");

        reviews.MapPost("/", async ([FromBody] SubmitReviewRequest request, IReviewService service, CancellationToken ct) =>
                (await service.SubmitAsync(request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithSummary("Submit a review. Flags verified purchase automatically.");

        reviews.MapPost("/{id:guid}/helpful", async (Guid id, IReviewService service, CancellationToken ct) =>
                (await service.MarkHelpfulAsync(id, ct)).ToHttpResult())
            .RequireAuthorization();

        var admin = app.MapGroup("/api/admin/reviews").WithTags("Admin · Reviews");

        admin.MapGet("/", async (
                    IReviewService service,
                    CancellationToken ct,
                    ReviewStatus? status = null,
                    Guid? productId = null,
                    int? rating = null,
                    string? search = null,
                    int page = 1,
                    int pageSize = 24) =>
                Results.Ok(await service.ListAsync(new ReviewQuery
                {
                    Status = status,
                    ProductId = productId,
                    Rating = rating,
                    Search = search,
                    Page = page,
                    PageSize = pageSize
                }, ct)))
            .RequirePermission(Permissions.Reviews.View)
            .WithSummary("Moderation queue — pending first.");

        admin.MapPut("/{id:guid}/moderate", async (Guid id, [FromBody] ModerateReviewRequest request, IReviewService service, CancellationToken ct) =>
                (await service.ModerateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Reviews.Moderate);

        admin.MapPut("/{id:guid}/reply", async (Guid id, [FromBody] ReplyToReviewRequest request, IReviewService service, CancellationToken ct) =>
                (await service.ReplyAsync(id, request.Reply, ct)).ToHttpResult())
            .RequirePermission(Permissions.Reviews.Reply);

        admin.MapDelete("/{id:guid}", async (Guid id, IReviewService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Reviews.Delete);
    }

    // ==========================================================================================
    // Inventory
    // ==========================================================================================

    private static void MapInventory(IEndpointRouteBuilder app)
    {
        var inventory = app.MapGroup("/api/admin/inventory").WithTags("Admin · Inventory");

        inventory.MapGet("/", async (
                    IInventoryService service,
                    CancellationToken ct,
                    string? search = null,
                    Guid? categoryId = null,
                    bool? lowStock = null,
                    bool? outOfStock = null,
                    string? sortBy = null,
                    bool descending = false,
                    int page = 1,
                    int pageSize = 24) =>
                Results.Ok(await service.GetStockLevelsAsync(new InventoryQuery
                {
                    Search = search,
                    CategoryId = categoryId,
                    LowStockOnly = lowStock,
                    OutOfStockOnly = outOfStock,
                    SortBy = sortBy,
                    Descending = descending,
                    Page = page,
                    PageSize = pageSize
                }, ct)))
            .RequirePermission(Permissions.Inventory.View);

        inventory.MapGet("/low-stock", async (IInventoryService service, CancellationToken ct, int take = 20) =>
                Results.Ok(await service.GetLowStockAsync(take, ct)))
            .RequirePermission(Permissions.Inventory.View);

        inventory.MapGet("/{variantId:guid}/history", async (
                    Guid variantId, IInventoryService service, CancellationToken ct, int page = 1, int pageSize = 50) =>
                Results.Ok(await service.GetHistoryAsync(variantId, page, pageSize, ct)))
            .RequirePermission(Permissions.Inventory.ViewHistory)
            .WithSummary("Append-only stock ledger for one variant.");

        inventory.MapPost("/adjust", async ([FromBody] AdjustStockRequest request, IInventoryService service, CancellationToken ct) =>
                (await service.AdjustAsync(request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Inventory.Adjust)
            .WithSummary("Set a variant's stock to a counted quantity. Reason is required.");

        inventory.MapPost("/adjust/bulk", async ([FromBody] BulkAdjustRequest request, IInventoryService service, CancellationToken ct) =>
                (await service.BulkAdjustAsync(request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Inventory.Adjust)
            .WithSummary("Apply a stock take. All-or-nothing.");
    }

    // ==========================================================================================
    // Reporting
    // ==========================================================================================

    private static void MapReporting(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/admin/reports").WithTags("Admin · Reports");

        reports.MapGet("/dashboard", async (IReportingService service, CancellationToken ct) =>
                Results.Ok(await service.GetDashboardAsync(ct)))
            .RequirePermission(Permissions.Reports.View)
            .WithSummary("Headline KPIs, 30-day sales trend, top products, recent orders.");

        reports.MapGet("/sales", async (
                    IReportingService service,
                    CancellationToken ct,
                    DateTimeOffset? from = null,
                    DateTimeOffset? to = null) =>
                Results.Ok(await service.GetSalesReportAsync(
                    from ?? DateTimeOffset.UtcNow.AddDays(-30),
                    to ?? DateTimeOffset.UtcNow,
                    ct)))
            .RequirePermission(Permissions.Reports.View)
            .WithSummary("Sales over a date range, by day, product and category.");
    }
}

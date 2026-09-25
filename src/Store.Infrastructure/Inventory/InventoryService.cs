using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Domain.Inventory;
using Store.Infrastructure.Commerce;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Inventory;

public sealed record StockLevelDto(
    Guid VariantId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? VariantName,
    string Sku,
    string? ThumbnailUrl,
    int StockQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int LowStockThreshold,
    bool TrackInventory,
    bool IsLowStock,
    bool IsOutOfStock,
    decimal Price,
    decimal? CostPrice,
    string Unit);

public sealed record StockMovementDto(
    Guid Id,
    InventoryTransactionType Type,
    int QuantityChange,
    int QuantityAfter,
    string? Reference,
    string? Note,
    string? PerformedByName,
    DateTimeOffset CreatedAt);

public sealed record AdjustStockRequest(Guid VariantId, int NewQuantity, string Reason);

public sealed record BulkAdjustRequest(IReadOnlyList<AdjustStockRequest> Adjustments);

public sealed record InventoryQuery : PagedQuery
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? LowStockOnly { get; init; }
    public bool? OutOfStockOnly { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
}

public interface IInventoryService
{
    Task<PagedResult<StockLevelDto>> GetStockLevelsAsync(InventoryQuery query, CancellationToken ct = default);
    Task<PagedResult<StockMovementDto>> GetHistoryAsync(Guid variantId, int page, int pageSize, CancellationToken ct = default);
    Task<Result> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default);
    Task<Result> BulkAdjustAsync(BulkAdjustRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StockLevelDto>> GetLowStockAsync(int take = 20, CancellationToken ct = default);
}

/// <summary>
/// Stock levels and manual adjustments.
/// </summary>
/// <remarks>
/// Adjustments are expressed as a <b>target quantity</b> ("the shelf has 47"), not a delta
/// ("add 3"). That matches how a stock take actually happens and removes a whole class of
/// double-application error. The delta is derived and written to the append-only ledger, so the
/// figure stays explainable afterwards.
/// </remarks>
public sealed class InventoryService(
    StoreDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    ICacheService cache,
    IDateTimeProvider clock,
    ILogger<InventoryService> logger) : IInventoryService
{
    public async Task<PagedResult<StockLevelDto>> GetStockLevelsAsync(
        InventoryQuery query, CancellationToken ct = default)
    {
        var variants = db.ProductVariants.AsNoTracking().Where(v => v.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            variants = variants.Where(v =>
                v.Sku.Contains(term) || v.Product.Name.Contains(term) || v.Barcode == term);
        }

        if (query.CategoryId is { } categoryId)
        {
            variants = variants.Where(v => v.Product.CategoryId == categoryId);
        }

        if (query.LowStockOnly == true)
        {
            variants = variants.Where(v => v.TrackInventory
                                           && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold);
        }

        if (query.OutOfStockOnly == true)
        {
            variants = variants.Where(v => v.TrackInventory && v.StockQuantity - v.ReservedQuantity <= 0);
        }

        variants = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => query.Descending ? variants.OrderByDescending(v => v.Product.Name) : variants.OrderBy(v => v.Product.Name),
            "sku" => query.Descending ? variants.OrderByDescending(v => v.Sku) : variants.OrderBy(v => v.Sku),
            "value" => query.Descending
                ? variants.OrderByDescending(v => v.StockQuantity * v.Price)
                : variants.OrderBy(v => v.StockQuantity * v.Price),
            // Default: the lowest stock first, because that is what needs attention.
            _ => query.Descending
                ? variants.OrderByDescending(v => v.StockQuantity)
                : variants.OrderBy(v => v.StockQuantity)
        };

        var total = await variants.CountAsync(ct);

        var items = await variants
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(v => new StockLevelDto(
                v.Id,
                v.ProductId,
                v.Product.Name,
                v.Product.Slug,
                v.Name,
                v.Sku,
                v.Product.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ThumbnailUrl).FirstOrDefault(),
                v.StockQuantity,
                v.ReservedQuantity,
                v.StockQuantity - v.ReservedQuantity,
                v.LowStockThreshold,
                v.TrackInventory,
                v.TrackInventory && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold,
                v.TrackInventory && v.StockQuantity - v.ReservedQuantity <= 0,
                v.Price,
                v.CostPrice,
                v.Unit))
            .ToListAsync(ct);

        return new PagedResult<StockLevelDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<StockMovementDto>> GetHistoryAsync(
        Guid variantId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var movements = db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ProductVariantId == variantId);

        var total = await movements.CountAsync(ct);

        // Joined to Users so the log reads "adjusted by Ayesha" rather than showing a raw GUID.
        var items = await movements
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new StockMovementDto(
                t.Id,
                t.Type,
                t.QuantityChange,
                t.QuantityAfter,
                t.Reference,
                t.Note,
                db.Users.Where(u => u.Id == t.PerformedBy)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<StockMovementDto>(items, page, pageSize, total);
    }

    public async Task<Result> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        if (request.NewQuantity < 0)
        {
            return Result.Failure("Stock cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            // Required, not optional. An unexplained stock change is exactly the kind of entry
            // that makes a discrepancy impossible to investigate three weeks later.
            return Result.Failure("Give a reason for the adjustment.");
        }

        var variant = await db.ProductVariants
            .Include(v => v.Product).ThenInclude(p => p.Variants)
            .FirstOrDefaultAsync(v => v.Id == request.VariantId, ct);

        if (variant is null)
        {
            return Result.NotFound("That product variant does not exist.");
        }

        var delta = request.NewQuantity - variant.StockQuantity;

        if (delta == 0)
        {
            return Result.Success();
        }

        variant.StockQuantity = request.NewQuantity;

        db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductVariantId = variant.Id,
            Type = InventoryTransactionType.Adjustment,
            QuantityChange = delta,
            QuantityAfter = request.NewQuantity,
            Note = request.Reason.Trim(),
            PerformedBy = currentUser.UserId,
            CreatedAt = clock.UtcNow
        });

        // The product's InStock/TotalStock columns drive the storefront's availability filter, so
        // they must move in the same transaction as the stock itself.
        Catalog.ProductService.RecalculateAggregates(variant.Product);

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);

        if (variant.TrackInventory && variant.AvailableQuantity <= variant.LowStockThreshold)
        {
            await notifications.NotifyLowStockAsync(
                variant.Id, variant.Product.Name, Math.Max(0, variant.AvailableQuantity), ct);
        }

        logger.LogInformation(
            "Stock adjusted for {Sku}: {Delta:+#;-#;0} → {NewQuantity} ({Reason})",
            variant.Sku, delta, request.NewQuantity, request.Reason);

        return Result.Success();
    }

    /// <summary>
    /// Applies several adjustments as one unit — the stock-take case.
    /// </summary>
    /// <remarks>
    /// Wrapped in a transaction so a stock take is all-or-nothing. A partially applied count is
    /// worse than none at all, because it looks complete.
    /// </remarks>
    public async Task<Result> BulkAdjustAsync(BulkAdjustRequest request, CancellationToken ct = default)
    {
        if (request.Adjustments.Count == 0)
        {
            return Result.Failure("No adjustments were supplied.");
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            foreach (var adjustment in request.Adjustments)
            {
                var result = await AdjustAsync(adjustment, ct);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }
            }

            await transaction.CommitAsync(ct);
            logger.LogInformation("Bulk stock adjustment applied to {Count} variants", request.Adjustments.Count);

            return Result.Success();
        });
    }

    public async Task<IReadOnlyList<StockLevelDto>> GetLowStockAsync(
        int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        var result = await GetStockLevelsAsync(new InventoryQuery
        {
            LowStockOnly = true,
            Page = 1,
            PageSize = take
        }, ct);

        return result.Items;
    }
}

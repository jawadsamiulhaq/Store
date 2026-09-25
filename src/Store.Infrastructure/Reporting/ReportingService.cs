using Microsoft.EntityFrameworkCore;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Reporting;

/// <summary>Headline figures for the admin dashboard.</summary>
public sealed record DashboardDto(
    decimal RevenueToday,
    decimal RevenueThisMonth,
    decimal RevenueAllTime,
    int OrdersToday,
    int OrdersThisMonth,
    int PendingOrders,
    int ProcessingOrders,
    int TotalProducts,
    int ActiveProducts,
    int OutOfStockCount,
    int LowStockCount,
    int TotalCustomers,
    int NewCustomersThisMonth,
    int PendingReviews,
    decimal AverageOrderValue,
    IReadOnlyList<SalesPointDto> SalesTrend,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<RecentOrderDto> RecentOrders);

public sealed record SalesPointDto(DateOnly Date, decimal Revenue, int Orders);

public sealed record TopProductDto(
    Guid ProductId, string Name, string Slug, string? ThumbnailUrl, int UnitsSold, decimal Revenue);

public sealed record RecentOrderDto(
    Guid Id, string OrderNumber, string CustomerName, decimal GrandTotal, OrderStatus Status, DateTimeOffset PlacedAt);

public sealed record SalesReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal GrossRevenue,
    decimal Discounts,
    decimal Shipping,
    decimal NetRevenue,
    int OrderCount,
    int UnitsSold,
    decimal AverageOrderValue,
    IReadOnlyList<SalesPointDto> Series,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<CategorySalesDto> ByCategory);

public sealed record CategorySalesDto(string Category, int UnitsSold, decimal Revenue);

public interface IReportingService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<SalesReportDto> GetSalesReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>
/// Dashboard and sales reporting.
/// </summary>
/// <remarks>
/// Every figure here is an aggregate computed in SQL — no report loads rows to count them in
/// memory. Revenue deliberately excludes cancelled and refunded orders throughout, so the number
/// on the dashboard is money the shop actually kept rather than money that was once quoted.
/// </remarks>
public sealed class ReportingService(
    StoreDbContext db,
    IDateTimeProvider clock) : IReportingService
{
    /// <summary>
    /// Statuses that count as real revenue. Cancelled and Refunded are excluded — counting them
    /// would overstate takings, and quietly.
    /// </summary>
    private static readonly OrderStatus[] RevenueStatuses =
    [
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing,
        OrderStatus.Shipped, OrderStatus.Delivered
    ];

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var todayStart = new DateTimeOffset(now.Date, now.Offset);
        var monthStart = new DateTimeOffset(new DateTime(now.Year, now.Month, 1), now.Offset);
        var trendStart = todayStart.AddDays(-29);

        var revenueOrders = db.Orders.AsNoTracking().Where(o => RevenueStatuses.Contains(o.Status));

        // One grouped query for every order-derived headline figure, rather than a query per tile.
        var orderStats = await revenueOrders
            .GroupBy(_ => 1)
            .Select(g => new
            {
                RevenueToday = g.Where(o => o.PlacedAt >= todayStart).Sum(o => (decimal?)o.GrandTotal) ?? 0,
                RevenueMonth = g.Where(o => o.PlacedAt >= monthStart).Sum(o => (decimal?)o.GrandTotal) ?? 0,
                RevenueAll = g.Sum(o => (decimal?)o.GrandTotal) ?? 0,
                OrdersToday = g.Count(o => o.PlacedAt >= todayStart),
                OrdersMonth = g.Count(o => o.PlacedAt >= monthStart),
                OrdersAll = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        var queueStats = await db.Orders
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pending = g.Count(o => o.Status == OrderStatus.Pending),
                Processing = g.Count(o => o.Status == OrderStatus.Processing || o.Status == OrderStatus.Confirmed)
            })
            .FirstOrDefaultAsync(ct);

        var productStats = await db.Products
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(p => p.Status == ProductStatus.Active),
                OutOfStock = g.Count(p => p.Status == ProductStatus.Active && !p.InStock)
            })
            .FirstOrDefaultAsync(ct);

        var lowStockCount = await db.ProductVariants
            .AsNoTracking()
            .CountAsync(v => v.IsActive
                             && v.TrackInventory
                             && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold, ct);

        var customerStats = await db.Customers
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                NewThisMonth = g.Count(c => c.CreatedAt >= monthStart)
            })
            .FirstOrDefaultAsync(ct);

        var pendingReviews = await db.Reviews
            .AsNoTracking()
            .CountAsync(r => r.Status == ReviewStatus.Pending, ct);

        // Grouped in SQL by date, then gaps are filled client-side so the chart has a point for
        // every day rather than skipping quiet ones (which would distort the line).
        var rawTrend = await revenueOrders
            .Where(o => o.PlacedAt >= trendStart)
            .GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.GrandTotal), Orders = g.Count() })
            .ToListAsync(ct);

        var trendByDate = rawTrend.ToDictionary(t => DateOnly.FromDateTime(t.Date));

        var salesTrend = Enumerable.Range(0, 30)
            .Select(offset =>
            {
                var date = DateOnly.FromDateTime(trendStart.AddDays(offset).Date);
                return trendByDate.TryGetValue(date, out var point)
                    ? new SalesPointDto(date, point.Revenue, point.Orders)
                    : new SalesPointDto(date, 0m, 0);
            })
            .ToList();

        var topProducts = await TopProductsAsync(monthStart, now, 5, ct);

        var recentOrders = await db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.PlacedAt)
            .Take(8)
            .Select(o => new RecentOrderDto(
                o.Id, o.OrderNumber, o.ShippingAddress.FullName, o.GrandTotal, o.Status, o.PlacedAt))
            .ToListAsync(ct);

        var revenueAll = orderStats?.RevenueAll ?? 0;
        var ordersAll = orderStats?.OrdersAll ?? 0;

        return new DashboardDto(
            orderStats?.RevenueToday ?? 0,
            orderStats?.RevenueMonth ?? 0,
            revenueAll,
            orderStats?.OrdersToday ?? 0,
            orderStats?.OrdersMonth ?? 0,
            queueStats?.Pending ?? 0,
            queueStats?.Processing ?? 0,
            productStats?.Total ?? 0,
            productStats?.Active ?? 0,
            productStats?.OutOfStock ?? 0,
            lowStockCount,
            customerStats?.Total ?? 0,
            customerStats?.NewThisMonth ?? 0,
            pendingReviews,
            ordersAll > 0 ? Math.Round(revenueAll / ordersAll, 2) : 0m,
            salesTrend,
            topProducts,
            recentOrders);
    }

    public async Task<SalesReportDto> GetSalesReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Guard the range rather than trusting it: an inverted or absurd range would otherwise
        // scan the whole Orders table.
        if (to < from)
        {
            (from, to) = (to, from);
        }

        if ((to - from).TotalDays > 400)
        {
            from = to.AddDays(-400);
        }

        var orders = db.Orders
            .AsNoTracking()
            .Where(o => RevenueStatuses.Contains(o.Status) && o.PlacedAt >= from && o.PlacedAt <= to);

        var totals = await orders
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gross = g.Sum(o => (decimal?)o.Subtotal) ?? 0,
                Discounts = g.Sum(o => (decimal?)o.DiscountTotal) ?? 0,
                Shipping = g.Sum(o => (decimal?)o.ShippingTotal) ?? 0,
                Net = g.Sum(o => (decimal?)o.GrandTotal) ?? 0,
                Count = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        var unitsSold = await db.OrderItems
            .AsNoTracking()
            .Where(i => RevenueStatuses.Contains(i.Order.Status)
                        && i.Order.PlacedAt >= from && i.Order.PlacedAt <= to)
            .SumAsync(i => (int?)i.Quantity, ct) ?? 0;

        var series = await orders
            .GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.GrandTotal), Orders = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var byCategory = await db.OrderItems
            .AsNoTracking()
            .Where(i => RevenueStatuses.Contains(i.Order.Status)
                        && i.Order.PlacedAt >= from && i.Order.PlacedAt <= to
                        && i.ProductId != null)
            .GroupBy(i => db.Products
                .Where(p => p.Id == i.ProductId)
                .Select(p => p.Category!.Name)
                .FirstOrDefault() ?? "Uncategorised")
            .Select(g => new CategorySalesDto(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(c => c.Revenue)
            .Take(20)
            .ToListAsync(ct);

        return new SalesReportDto(
            from,
            to,
            totals?.Gross ?? 0,
            totals?.Discounts ?? 0,
            totals?.Shipping ?? 0,
            totals?.Net ?? 0,
            totals?.Count ?? 0,
            unitsSold,
            totals is { Count: > 0 } ? Math.Round(totals.Net / totals.Count, 2) : 0m,
            [.. series.Select(s => new SalesPointDto(DateOnly.FromDateTime(s.Date), s.Revenue, s.Orders))],
            await TopProductsAsync(from, to, 10, ct),
            byCategory);
    }

    /// <summary>
    /// Best sellers over a window, computed from order lines rather than the denormalised
    /// <c>SalesCount</c> — that column is lifetime, and a report needs the period.
    /// </summary>
    private async Task<List<TopProductDto>> TopProductsAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct)
    {
        var aggregated = await db.OrderItems
            .AsNoTracking()
            .Where(i => RevenueStatuses.Contains(i.Order.Status)
                        && i.Order.PlacedAt >= from && i.Order.PlacedAt <= to
                        && i.ProductId != null)
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new
            {
                ProductId = g.Key,
                Units = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(x => x.Units)
            .Take(take)
            .ToListAsync(ct);

        if (aggregated.Count == 0)
        {
            return [];
        }

        // Names and images resolved in one follow-up query over at most `take` ids.
        var ids = aggregated.Select(a => a.ProductId).ToList();

        var details = await db.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                Thumbnail = p.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ThumbnailUrl).FirstOrDefault()
            })
            .ToDictionaryAsync(p => p.Id, ct);

        return [.. aggregated
            .Where(a => details.ContainsKey(a.ProductId))
            .Select(a => new TopProductDto(
                a.ProductId,
                details[a.ProductId].Name,
                details[a.ProductId].Slug,
                details[a.ProductId].Thumbnail,
                a.Units,
                a.Revenue))];
    }
}

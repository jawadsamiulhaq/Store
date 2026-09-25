using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Domain.Inventory;
using Store.Domain.Orders;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public interface IOrderService
{
    // ---- Customer ----
    Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(int page, int pageSize, CancellationToken ct = default);
    Task<Result<OrderDetailDto>> GetMyOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Public tracking by order number plus email — the guest-order lookup.</summary>
    Task<Result<OrderDetailDto>> TrackAsync(string orderNumber, string email, CancellationToken ct = default);

    Task<Result> CancelMyOrderAsync(Guid orderId, string reason, CancellationToken ct = default);

    // ---- Admin ----
    Task<PagedResult<AdminOrderListItemDto>> ListAsync(OrderQuery query, CancellationToken ct = default);
    Task<Result<OrderDetailDto>> GetAsync(Guid orderId, CancellationToken ct = default);
    Task<Result<OrderDetailDto>> UpdateStatusAsync(Guid orderId, UpdateOrderStatusRequest request, CancellationToken ct = default);
    Task<Result<OrderDetailDto>> AddShipmentAsync(Guid orderId, CreateShipmentRequest request, CancellationToken ct = default);
}

/// <summary>
/// Order retrieval and lifecycle.
/// </summary>
/// <remarks>
/// Customer-facing reads are scoped by ownership in the query itself — the customer id is part of
/// the <c>WHERE</c> clause, not checked afterwards — so guessing another customer's order id
/// returns "not found" rather than their data.
/// </remarks>
public sealed class OrderService(
    StoreDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IDateTimeProvider clock,
    ILogger<OrderService> logger) : IOrderService
{
    /// <summary>
    /// Transitions an order is allowed to make. Modelled explicitly so an order cannot jump from
    /// Pending straight to Delivered, or come back from Cancelled.
    /// </summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Refunded],
        [OrderStatus.Delivered] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Refunded] = []
    };

    // ==========================================================================================
    // Customer
    // ==========================================================================================

    public async Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var customerId = await ResolveCustomerIdAsync(ct);

        if (customerId is null)
        {
            return PagedResult<OrderSummaryDto>.Empty(page, pageSize);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(OrderProjections.Summary)
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, total);
    }

    public async Task<Result<OrderDetailDto>> GetMyOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var customerId = await ResolveCustomerIdAsync(ct);

        if (customerId is null)
        {
            return Result<OrderDetailDto>.NotFound("Order not found.");
        }

        // Ownership is part of the query. A customer cannot fetch someone else's order by id.
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.CustomerId == customerId)
            .Select(OrderProjections.Detail)
            .FirstOrDefaultAsync(ct);

        return order is null
            ? Result<OrderDetailDto>.NotFound("Order not found.")
            : Result<OrderDetailDto>.Success(order);
    }

    /// <summary>
    /// Guest order tracking. Requires both the order number and the email it was placed with, so
    /// knowing (or guessing) an order number alone discloses nothing.
    /// </summary>
    public async Task<Result<OrderDetailDto>> TrackAsync(
        string orderNumber, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(email))
        {
            return Result<OrderDetailDto>.Failure("Enter both your order number and email address.");
        }

        var normalisedEmail = email.Trim().ToLowerInvariant();
        var normalisedNumber = orderNumber.Trim().ToUpperInvariant();

        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNumber == normalisedNumber && o.Email == normalisedEmail)
            .Select(OrderProjections.Detail)
            .FirstOrDefaultAsync(ct);

        // One message whether the order is missing or the email does not match, so this cannot be
        // used to test which order numbers exist.
        return order is null
            ? Result<OrderDetailDto>.NotFound("We could not find an order with those details.")
            : Result<OrderDetailDto>.Success(order);
    }

    public async Task<Result> CancelMyOrderAsync(Guid orderId, string reason, CancellationToken ct = default)
    {
        var customerId = await ResolveCustomerIdAsync(ct);

        if (customerId is null)
        {
            return Result.NotFound("Order not found.");
        }

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId, ct);

        if (order is null)
        {
            return Result.NotFound("Order not found.");
        }

        if (!order.IsCancellable)
        {
            return Result.Conflict(
                $"This order is already {order.Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");
        }

        await ApplyCancellationAsync(order, reason, customerInitiated: true, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ==========================================================================================
    // Admin
    // ==========================================================================================

    public async Task<PagedResult<AdminOrderListItemDto>> ListAsync(
        OrderQuery query, CancellationToken ct = default)
    {
        var orders = db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            orders = orders.Where(o =>
                o.OrderNumber.Contains(term) ||
                o.Email.Contains(term) ||
                o.Phone.Contains(term) ||
                o.ShippingAddress.FullName.Contains(term));
        }

        if (query.Status is { } status) orders = orders.Where(o => o.Status == status);
        if (query.PaymentStatus is { } payment) orders = orders.Where(o => o.PaymentStatus == payment);
        if (query.CustomerId is { } customer) orders = orders.Where(o => o.CustomerId == customer);
        if (query.From is { } from) orders = orders.Where(o => o.PlacedAt >= from);
        if (query.To is { } to) orders = orders.Where(o => o.PlacedAt <= to);

        orders = query.SortBy?.ToLowerInvariant() switch
        {
            "total" => query.Descending ? orders.OrderByDescending(o => o.GrandTotal) : orders.OrderBy(o => o.GrandTotal),
            "status" => query.Descending ? orders.OrderByDescending(o => o.Status) : orders.OrderBy(o => o.Status),
            _ => query.Descending ? orders.OrderByDescending(o => o.PlacedAt) : orders.OrderBy(o => o.PlacedAt)
        };

        var total = await orders.CountAsync(ct);

        var items = await orders
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(OrderProjections.AdminListItem)
            .ToListAsync(ct);

        return new PagedResult<AdminOrderListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<Result<OrderDetailDto>> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(OrderProjections.Detail)
            .FirstOrDefaultAsync(ct);

        return order is null
            ? Result<OrderDetailDto>.NotFound("Order not found.")
            : Result<OrderDetailDto>.Success(order);
    }

    public async Task<Result<OrderDetailDto>> UpdateStatusAsync(
        Guid orderId, UpdateOrderStatusRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            return Result<OrderDetailDto>.NotFound("Order not found.");
        }

        if (order.Status == request.Status)
        {
            return await GetAsync(orderId, ct);
        }

        // Enforced server-side. The admin UI hides invalid transitions, but a direct call must not
        // be able to move an order backwards or skip fulfilment steps.
        if (!AllowedTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(request.Status))
        {
            return Result<OrderDetailDto>.Conflict(
                $"An order cannot go from {order.Status} to {request.Status}.");
        }

        var previous = order.Status;

        if (request.Status == OrderStatus.Cancelled)
        {
            await ApplyCancellationAsync(order, request.Note ?? "Cancelled by staff", customerInitiated: false, ct);
        }
        else
        {
            order.Status = request.Status;

            switch (request.Status)
            {
                case OrderStatus.Confirmed:
                    order.ConfirmedAt = clock.UtcNow;
                    break;

                case OrderStatus.Shipped:
                    order.ShippedAt = clock.UtcNow;
                    order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                    break;

                case OrderStatus.Delivered:
                    order.DeliveredAt = clock.UtcNow;
                    order.FulfillmentStatus = FulfillmentStatus.Fulfilled;

                    // Cash on delivery is only actually paid once it is delivered.
                    if (order.PaymentMethod == PaymentMethod.CashOnDelivery)
                    {
                        order.PaymentStatus = PaymentStatus.Paid;
                    }
                    break;

                case OrderStatus.Refunded:
                    order.PaymentStatus = PaymentStatus.Refunded;
                    await RestoreStockAsync(order, "Refunded", ct);
                    break;
            }

            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = previous,
                ToStatus = request.Status,
                Note = request.Note,
                ChangedBy = currentUser.UserId,
                CreatedAt = clock.UtcNow,
                IsCustomerVisible = true
            });
        }

        await db.SaveChangesAsync(ct);

        if (request.NotifyCustomer)
        {
            await notifications.NotifyOrderStatusAsync(order, ct);
        }

        logger.LogInformation(
            "Order {OrderNumber} moved {From} → {To} by {UserId}",
            order.OrderNumber, previous, request.Status, currentUser.UserId);

        return await GetAsync(orderId, ct);
    }

    public async Task<Result<OrderDetailDto>> AddShipmentAsync(
        Guid orderId, CreateShipmentRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            return Result<OrderDetailDto>.NotFound("Order not found.");
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return Result<OrderDetailDto>.Conflict("A cancelled or refunded order cannot be shipped.");
        }

        db.Shipments.Add(new Shipment
        {
            OrderId = orderId,
            Carrier = request.Carrier.Trim(),
            TrackingNumber = request.TrackingNumber,
            TrackingUrl = request.TrackingUrl,
            EstimatedDeliveryAt = request.EstimatedDeliveryAt,
            Note = request.Note,
            Status = ShipmentStatus.InTransit,
            ShippedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow
        });

        // Adding tracking is the act of shipping, so the order moves with it rather than needing
        // a second, easily-forgotten status update.
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Processing)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = order.Status,
                ToStatus = OrderStatus.Shipped,
                Note = $"Shipped with {request.Carrier}",
                ChangedBy = currentUser.UserId,
                CreatedAt = clock.UtcNow,
                IsCustomerVisible = true
            });

            order.Status = OrderStatus.Shipped;
            order.ShippedAt = clock.UtcNow;
            order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
        }

        await db.SaveChangesAsync(ct);
        await notifications.NotifyOrderStatusAsync(order, ct);

        return await GetAsync(orderId, ct);
    }

    // ==========================================================================================
    // Internals
    // ==========================================================================================

    private async Task ApplyCancellationAsync(
        Order order, string reason, bool customerInitiated, CancellationToken ct)
    {
        var previous = order.Status;

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = clock.UtcNow;
        order.CancelReason = reason;

        // Stock goes back on the shelf. Forgetting this is how a store ends up unable to sell
        // items it physically has.
        await RestoreStockAsync(order, $"Order {order.OrderNumber} cancelled", ct);

        // Lifetime totals must not count an order that was never fulfilled.
        if (order.CustomerId is { } customerId)
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);

            if (customer is not null)
            {
                customer.TotalOrders = Math.Max(0, customer.TotalOrders - 1);
                customer.TotalSpent = Math.Max(0, customer.TotalSpent - order.GrandTotal);
            }
        }

        // Release the coupon so the shopper can use it again.
        if (order.CouponId is { } couponId)
        {
            var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == couponId, ct);

            if (coupon is not null)
            {
                coupon.UsedCount = Math.Max(0, coupon.UsedCount - 1);
            }

            var redemption = await db.CouponRedemptions
                .FirstOrDefaultAsync(r => r.OrderId == order.Id, ct);

            if (redemption is not null)
            {
                db.CouponRedemptions.Remove(redemption);
            }
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = previous,
            ToStatus = OrderStatus.Cancelled,
            Note = customerInitiated ? $"Cancelled by customer: {reason}" : reason,
            ChangedBy = currentUser.UserId,
            CreatedAt = clock.UtcNow,
            IsCustomerVisible = true
        });

        logger.LogInformation("Order {OrderNumber} cancelled: {Reason}", order.OrderNumber, reason);
    }

    /// <summary>Returns an order's items to sellable stock and records the movement in the ledger.</summary>
    private async Task RestoreStockAsync(Order order, string note, CancellationToken ct)
    {
        var items = order.Items.Count > 0
            ? order.Items.ToList()
            : await db.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync(ct);

        foreach (var item in items.Where(i => i.ProductVariantId is not null))
        {
            var variant = await db.ProductVariants
                .Include(v => v.Product).ThenInclude(p => p.Variants)
                .FirstOrDefaultAsync(v => v.Id == item.ProductVariantId, ct);

            if (variant is null || !variant.TrackInventory)
            {
                continue;
            }

            variant.StockQuantity += item.Quantity;

            db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductVariantId = variant.Id,
                Type = InventoryTransactionType.Return,
                QuantityChange = item.Quantity,
                QuantityAfter = variant.StockQuantity,
                Reference = order.OrderNumber,
                OrderId = order.Id,
                Note = note,
                PerformedBy = currentUser.UserId,
                CreatedAt = clock.UtcNow
            });

            variant.Product.SalesCount = Math.Max(0, variant.Product.SalesCount - item.Quantity);
            Catalog.ProductService.RecalculateAggregates(variant.Product);
        }
    }

    private async Task<Guid?> ResolveCustomerIdAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        return await db.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }
}

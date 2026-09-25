using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Customers;
using Store.Domain.Enums;
using Store.Domain.Inventory;
using Store.Domain.Orders;
using Store.Domain.Promotions;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public interface ICheckoutService
{
    /// <summary>Prices the basket, including shipping options, before the order is placed.</summary>
    Task<Result<CheckoutSummaryDto>> GetSummaryAsync(
        string? region, Guid? shippingMethodId, CancellationToken ct = default);

    Task<Result<OrderDetailDto>> PlaceOrderAsync(CheckoutRequest request, CancellationToken ct = default);
}

/// <summary>
/// Turns a cart into an order.
/// </summary>
/// <remarks>
/// The most correctness-critical code in the application, and written defensively because of it:
/// <list type="bullet">
///   <item>Everything happens inside <b>one database transaction</b>. A half-placed order — stock
///   taken but no order row, or an order with no stock movement — is not a state the system can
///   be left in.</item>
///   <item><b>Prices are recomputed from the live catalogue</b>, never taken from the cart or the
///   client. The cart is user-controlled input.</item>
///   <item><b>Stock is decremented under optimistic concurrency</b>, and the whole transaction
///   fails rather than overselling.</item>
///   <item><b>The coupon is re-validated</b> at placement, not trusted from when it was applied.</item>
/// </list>
/// </remarks>
public sealed class CheckoutService(
    StoreDbContext db,
    ICurrentUser currentUser,
    ICustomerContext customers,
    IDiscountService discounts,
    IShippingService shipping,
    INotificationService notifications,
    IDateTimeProvider clock,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    public async Task<Result<CheckoutSummaryDto>> GetSummaryAsync(
        string? region, Guid? shippingMethodId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(ct);

        if (cart is null || cart.Items.Count == 0)
        {
            return Result<CheckoutSummaryDto>.Failure("Your cart is empty.");
        }

        var blockers = new List<string>();
        var items = new List<CartItemDto>();
        var subtotal = 0m;
        var weightKg = 0m;

        foreach (var item in cart.Items)
        {
            var variant = item.ProductVariant;
            var product = variant.Product;

            if (product.Status != ProductStatus.Active || !variant.IsActive)
            {
                blockers.Add($"{product.Name} is no longer available.");
                continue;
            }

            if (variant.TrackInventory && !variant.AllowBackorder && variant.AvailableQuantity < item.Quantity)
            {
                blockers.Add(variant.AvailableQuantity == 0
                    ? $"{product.Name} is out of stock."
                    : $"Only {variant.AvailableQuantity} of {product.Name} remain.");
            }

            var line = variant.Price * item.Quantity;
            subtotal += line;
            weightKg += (variant.WeightGrams ?? 0) * item.Quantity / 1000m;

            items.Add(new CartItemDto(
                item.Id, variant.Id, variant.ProductId, product.Name, product.Slug,
                variant.Name, variant.Sku, null, variant.Price, variant.CompareAtPrice,
                item.Quantity, line, variant.AvailableQuantity, true,
                variant.Unit, variant.UnitValue, item.UnitPriceSnapshot));
        }

        AppliedCouponDto? coupon = null;
        var discountTotal = 0m;
        var freeShipping = false;

        if (cart.Coupon is not null)
        {
            var validation = await discounts.ValidateAsync(cart.Coupon.Code, subtotal, currentUser.UserId, ct);

            if (validation.Succeeded)
            {
                discountTotal = validation.Required.DiscountAmount;
                freeShipping = validation.Required.IsFreeShipping;
                coupon = new AppliedCouponDto(
                    cart.Coupon.Code, cart.Coupon.Description,
                    cart.Coupon.DiscountType, cart.Coupon.Value, discountTotal);
            }
            else
            {
                blockers.Add($"Coupon {cart.Coupon.Code}: {validation.Error}");
            }
        }

        var options = await shipping.GetQuotesAsync(region, subtotal - discountTotal, weightKg, ct);

        if (freeShipping)
        {
            options = [.. options.Select(o => o with { Rate = 0m, IsFree = true })];
        }

        var chosen = shippingMethodId is { } id
            ? options.FirstOrDefault(o => o.MethodId == id)
            : options.FirstOrDefault();

        var shippingTotal = chosen?.Rate ?? 0m;

        var minimum = await GetMinimumOrderAmountAsync(ct);

        if (minimum > 0 && subtotal < minimum)
        {
            blockers.Add($"The minimum order is {minimum:N0}. Add {minimum - subtotal:N0} more to check out.");
        }

        return Result<CheckoutSummaryDto>.Success(new CheckoutSummaryDto(
            items,
            subtotal,
            discountTotal,
            shippingTotal,
            Math.Max(0, subtotal - discountTotal + shippingTotal),
            "HKD",
            coupon,
            options,
            blockers));
    }

    public async Task<Result<OrderDetailDto>> PlaceOrderAsync(
        CheckoutRequest request, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(ct);

        if (cart is null || cart.Items.Count == 0)
        {
            return Result<OrderDetailDto>.Failure("Your cart is empty.");
        }

        var customerId = await customers.GetOrCreateIdAsync(ct);

        var guestCheckoutAllowed = await GetSettingBoolAsync("checkout.guest-enabled", true, ct);

        if (customerId is null && !guestCheckoutAllowed)
        {
            return Result<OrderDetailDto>.Forbidden("Please sign in to complete your order.");
        }

        // A single transaction around the whole placement. Under the retrying execution strategy
        // the entire block must be re-runnable, which is why it is wrapped this way rather than
        // opening a bare transaction.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            try
            {
                var order = new Order
                {
                    OrderNumber = await GenerateOrderNumberAsync(ct),
                    CustomerId = customerId,
                    Email = request.Email.Trim().ToLowerInvariant(),
                    Phone = request.Phone.Trim(),
                    Status = OrderStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending,
                    FulfillmentStatus = FulfillmentStatus.Unfulfilled,
                    PaymentMethod = request.PaymentMethod,
                    CustomerNote = request.CustomerNote,
                    CurrencyCode = "HKD",
                    PlacedAt = clock.UtcNow,
                    ShippingAddress = ToOrderAddress(request.ShippingAddress),
                    BillingAddress = ToOrderAddress(request.BillingAddress ?? request.ShippingAddress)
                };

                var subtotal = 0m;
                var weightKg = 0m;

                foreach (var item in cart.Items)
                {
                    var variant = item.ProductVariant;
                    var product = variant.Product;

                    if (product.Status != ProductStatus.Active || !variant.IsActive)
                    {
                        await transaction.RollbackAsync(ct);
                        return Result<OrderDetailDto>.Conflict($"{product.Name} is no longer available.");
                    }

                    // Final stock check inside the transaction. Checking earlier is advisory;
                    // this is the one that counts.
                    if (variant.TrackInventory && !variant.AllowBackorder && variant.AvailableQuantity < item.Quantity)
                    {
                        await transaction.RollbackAsync(ct);
                        return Result<OrderDetailDto>.Conflict(
                            variant.AvailableQuantity == 0
                                ? $"{product.Name} sold out while you were checking out."
                                : $"Only {variant.AvailableQuantity} of {product.Name} remain.");
                    }

                    // Live price. Never item.UnitPriceSnapshot, which the shopper's session
                    // controls and which may be stale or tampered with.
                    var unitPrice = variant.Price;
                    var lineTotal = unitPrice * item.Quantity;
                    subtotal += lineTotal;
                    weightKg += (variant.WeightGrams ?? 0) * item.Quantity / 1000m;

                    var primaryImage = await db.ProductImages
                        .AsNoTracking()
                        .Where(i => i.ProductId == product.Id)
                        .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                        .Select(i => i.ThumbnailUrl ?? i.Url)
                        .FirstOrDefaultAsync(ct);

                    order.Items.Add(new OrderItem
                    {
                        ProductVariantId = variant.Id,
                        ProductId = product.Id,
                        // Snapshot: the order must still describe what was bought even if the
                        // catalogue changes or the product is deleted.
                        ProductName = product.Name,
                        VariantName = variant.Name,
                        Sku = variant.Sku,
                        ImageUrl = primaryImage,
                        ProductSlug = product.Slug,
                        UnitPrice = unitPrice,
                        Quantity = item.Quantity,
                        LineTotal = lineTotal
                    });

                    if (variant.TrackInventory)
                    {
                        variant.StockQuantity -= item.Quantity;

                        db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductVariantId = variant.Id,
                            Type = InventoryTransactionType.Sale,
                            QuantityChange = -item.Quantity,
                            QuantityAfter = variant.StockQuantity,
                            Reference = order.OrderNumber,
                            Note = "Sold",
                            CreatedAt = clock.UtcNow
                        });
                    }

                    product.SalesCount += item.Quantity;
                    Catalog.ProductService.RecalculateAggregates(product);
                }

                // Coupon re-validated here, against the freshly computed subtotal — not trusted
                // from when it was applied to the cart.
                var discountTotal = 0m;
                var freeShipping = false;
                Coupon? appliedCoupon = null;

                var couponCode = request.CouponCode ?? cart.Coupon?.Code;

                if (!string.IsNullOrWhiteSpace(couponCode))
                {
                    var validation = await discounts.ValidateAsync(couponCode, subtotal, currentUser.UserId, ct);

                    if (!validation.Succeeded)
                    {
                        await transaction.RollbackAsync(ct);
                        return Result<OrderDetailDto>.Failure($"Coupon {couponCode}: {validation.Error}");
                    }

                    discountTotal = validation.Required.DiscountAmount;
                    freeShipping = validation.Required.IsFreeShipping;

                    appliedCoupon = await db.Coupons.FirstAsync(c => c.Id == validation.Required.CouponId, ct);
                    appliedCoupon.UsedCount += 1;

                    order.CouponId = appliedCoupon.Id;
                    order.CouponCode = appliedCoupon.Code;

                    ApportionDiscount(order, discountTotal);
                }

                var method = await db.ShippingMethods
                    .Include(m => m.ShippingZone)
                    .FirstOrDefaultAsync(m => m.Id == request.ShippingMethodId && m.IsActive, ct);

                if (method is null)
                {
                    await transaction.RollbackAsync(ct);
                    return Result<OrderDetailDto>.Failure("Choose a delivery option.");
                }

                var shippingTotal = freeShipping ? 0m : method.CalculateRate(subtotal - discountTotal, weightKg);

                order.ShippingMethodId = method.Id;
                order.ShippingMethodName = $"{method.Name} ({method.ShippingZone.Name})";
                order.Subtotal = subtotal;
                order.DiscountTotal = discountTotal;
                order.ShippingTotal = shippingTotal;
                order.TaxTotal = 0m; // Hong Kong has no GST/VAT on groceries.
                order.GrandTotal = Math.Max(0, subtotal - discountTotal + shippingTotal);

                order.StatusHistory.Add(new OrderStatusHistory
                {
                    ToStatus = OrderStatus.Pending,
                    Note = "Order placed",
                    CreatedAt = clock.UtcNow,
                    IsCustomerVisible = true
                });

                db.Orders.Add(order);

                if (appliedCoupon is not null)
                {
                    db.CouponRedemptions.Add(new CouponRedemption
                    {
                        CouponId = appliedCoupon.Id,
                        OrderId = order.Id,
                        CustomerId = customerId,
                        DiscountAmount = discountTotal,
                        CreatedAt = clock.UtcNow
                    });
                }

                if (customerId is { } cid)
                {
                    var customer = await db.Customers.FirstAsync(c => c.Id == cid, ct);
                    customer.TotalOrders += 1;
                    customer.TotalSpent += order.GrandTotal;
                    customer.LastOrderAt = clock.UtcNow;

                    if (request.SaveAddress)
                    {
                        await SaveAddressAsync(cid, request.ShippingAddress, ct);
                    }
                }

                // The cart is consumed by the order.
                db.CartItems.RemoveRange(cart.Items);
                db.Carts.Remove(cart);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                logger.LogInformation(
                    "Order {OrderNumber} placed: {ItemCount} item(s), total {Total} {Currency}",
                    order.OrderNumber, order.Items.Count, order.GrandTotal, order.CurrencyCode);

                // Deliberately after the commit. Notifying inside the transaction would mean a
                // confirmation email for an order that could still roll back.
                await notifications.NotifyOrderPlacedAsync(order, ct);
                await RaiseLowStockAlertsAsync(order, ct);

                return await LoadOrderDetailAsync(order.Id, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another order took the same stock between our read and our write. Rolling back
                // is correct: overselling is worse than asking the shopper to try again.
                await transaction.RollbackAsync(ct);
                logger.LogWarning("Concurrency conflict while placing an order; stock changed mid-checkout");

                return Result<OrderDetailDto>.Conflict(
                    "Someone bought one of your items while you were checking out. Please review your cart and try again.");
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    // ==========================================================================================
    // Internals
    // ==========================================================================================

    /// <summary>
    /// Raises a low-stock alert for any variant the order pushed to or below its threshold.
    /// </summary>
    /// <remarks>
    /// Runs after the commit, and each failure is swallowed: an alert is operational convenience,
    /// and it must never be the reason a shopper sees an error on a successfully placed order.
    /// </remarks>
    private async Task RaiseLowStockAlertsAsync(Order order, CancellationToken ct)
    {
        try
        {
            var variantIds = order.Items
                .Where(i => i.ProductVariantId is not null)
                .Select(i => i.ProductVariantId!.Value)
                .ToList();

            var low = await db.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id)
                            && v.TrackInventory
                            && v.StockQuantity - v.ReservedQuantity <= v.LowStockThreshold)
                .Select(v => new
                {
                    v.Id,
                    v.Product.Name,
                    Remaining = v.StockQuantity - v.ReservedQuantity
                })
                .ToListAsync(ct);

            foreach (var variant in low)
            {
                await notifications.NotifyLowStockAsync(
                    variant.Id, variant.Name, Math.Max(0, variant.Remaining), ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to raise low-stock alerts for order {OrderNumber}", order.OrderNumber);
        }
    }

    /// <summary>
    /// Spreads an order-level discount across its lines, proportionally to line value.
    /// </summary>
    /// <remarks>
    /// Needed so that a per-line refund later returns the right amount. The rounding remainder is
    /// pushed onto the last line, so the apportioned parts always sum to exactly the discount —
    /// otherwise a $10.00 discount can silently become $9.99 across three lines.
    /// </remarks>
    private static void ApportionDiscount(Order order, decimal discountTotal)
    {
        if (discountTotal <= 0 || order.Items.Count == 0)
        {
            return;
        }

        var subtotal = order.Items.Sum(i => i.LineTotal);

        if (subtotal <= 0)
        {
            return;
        }

        var allocated = 0m;

        for (var i = 0; i < order.Items.Count; i++)
        {
            var item = order.Items.ElementAt(i);

            var share = i == order.Items.Count - 1
                ? discountTotal - allocated
                : Math.Round(discountTotal * (item.LineTotal / subtotal), 2, MidpointRounding.AwayFromZero);

            item.LineDiscount = share;
            item.LineTotal -= share;
            allocated += share;
        }
    }

    /// <summary>
    /// Builds a human-quotable reference: <c>WPS-20260923-0041</c>.
    /// </summary>
    /// <remarks>
    /// The sequence restarts daily and is padded, so it never reveals the store's lifetime order
    /// count to customers — a number competitors can read off a receipt.
    /// </remarks>
    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var today = clock.UtcNow.Date;
        var prefix = $"WPS-{clock.UtcNow:yyyyMMdd}";

        var todayCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.PlacedAt >= today, ct);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = $"{prefix}-{todayCount + attempt:D4}";

            if (!await db.Orders.AsNoTracking().AnyAsync(o => o.OrderNumber == candidate, ct))
            {
                return candidate;
            }
        }

        return $"{prefix}-{Guid.CreateVersion7().ToString("N")[..6].ToUpperInvariant()}";
    }

    private async Task<Domain.Carts.Cart?> LoadCartAsync(CancellationToken ct)
    {
        var customerId = await customers.GetIdAsync(ct);
        var anonymousId = currentUser.AnonymousId;

        if (customerId is null && string.IsNullOrWhiteSpace(anonymousId))
        {
            return null;
        }

        return await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => customerId != null
                ? c.CustomerId == customerId
                : c.AnonymousId == anonymousId, ct);
    }


    private async Task SaveAddressAsync(Guid customerId, OrderAddressRequest request, CancellationToken ct)
    {
        var duplicate = await db.Addresses.AnyAsync(a =>
            a.CustomerId == customerId &&
            a.Line1 == request.Line1 &&
            a.City == request.City, ct);

        if (duplicate)
        {
            return;
        }

        var isFirst = !await db.Addresses.AnyAsync(a => a.CustomerId == customerId, ct);

        db.Addresses.Add(new Address
        {
            CustomerId = customerId,
            FullName = request.FullName,
            Phone = request.Phone,
            Line1 = request.Line1,
            Line2 = request.Line2,
            District = request.District,
            City = request.City,
            Region = request.Region,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            IsDefaultShipping = isFirst,
            IsDefaultBilling = isFirst
        });
    }

    private static OrderAddress ToOrderAddress(OrderAddressRequest r) => new()
    {
        FullName = r.FullName.Trim(),
        Phone = r.Phone.Trim(),
        Line1 = r.Line1.Trim(),
        Line2 = r.Line2,
        District = r.District,
        City = r.City.Trim(),
        Region = r.Region,
        PostalCode = r.PostalCode,
        CountryCode = string.IsNullOrWhiteSpace(r.CountryCode) ? "HK" : r.CountryCode
    };

    private async Task<decimal> GetMinimumOrderAmountAsync(CancellationToken ct)
    {
        var raw = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == "checkout.min-order-amount")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return decimal.TryParse(raw, out var value) ? value : 0m;
    }

    private async Task<bool> GetSettingBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private async Task<Result<OrderDetailDto>> LoadOrderDetailAsync(Guid orderId, CancellationToken ct)
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
}

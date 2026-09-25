using Microsoft.EntityFrameworkCore;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

/// <summary>Outcome of validating a coupon against a specific basket and customer.</summary>
public sealed record CouponValidation(
    Guid CouponId,
    string Code,
    DiscountType DiscountType,
    decimal Value,
    decimal DiscountAmount,
    bool IsFreeShipping);

public interface IDiscountService
{
    /// <summary>
    /// Validates a coupon against a basket subtotal and a customer, returning the money it would
    /// actually discount.
    /// </summary>
    Task<Result<CouponValidation>> ValidateAsync(
        string code, decimal subtotal, Guid? userId, CancellationToken ct = default);
}

/// <summary>
/// The discount engine.
/// </summary>
/// <remarks>
/// Every rule is enforced here, server-side, on the live coupon row — the storefront's preview of
/// a discount is advisory only. Validation is deliberately re-run at checkout rather than trusting
/// what the cart recorded earlier: a coupon can expire, exhaust its usage limit, or stop meeting
/// its minimum spend between the moment it is applied and the moment the order is placed.
/// </remarks>
public sealed class DiscountService(
    StoreDbContext db,
    IDateTimeProvider clock) : IDiscountService
{
    public async Task<Result<CouponValidation>> ValidateAsync(
        string code, decimal subtotal, Guid? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<CouponValidation>.Failure("Enter a discount code.");
        }

        // Compared upper-case so "save10" and "SAVE10" are the same coupon.
        var normalised = code.Trim().ToUpperInvariant();

        var coupon = await db.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == normalised, ct);

        if (coupon is null)
        {
            return Result<CouponValidation>.Failure("That discount code is not recognised.");
        }

        if (!coupon.IsActive)
        {
            return Result<CouponValidation>.Failure("That discount code is no longer active.");
        }

        var now = clock.UtcNow;

        if (coupon.StartsAt is { } starts && starts > now)
        {
            return Result<CouponValidation>.Failure("That discount code is not valid yet.");
        }

        if (coupon.EndsAt is { } ends && ends < now)
        {
            return Result<CouponValidation>.Failure("That discount code has expired.");
        }

        if (coupon.UsageLimit is { } limit && coupon.UsedCount >= limit)
        {
            return Result<CouponValidation>.Failure("That discount code has reached its usage limit.");
        }

        if (coupon.MinOrderAmount is { } minimum && subtotal < minimum)
        {
            return Result<CouponValidation>.Failure(
                $"Spend at least {minimum:N0} to use this code.");
        }

        // Per-customer limits are counted from the redemption rows rather than a denormalised
        // counter, because the rows are the only reliable record of who used what.
        if (userId is { } id)
        {
            var customerId = await db.Customers
                .AsNoTracking()
                .Where(c => c.UserId == id)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (customerId is { } cid)
            {
                if (coupon.UsageLimitPerCustomer is { } perCustomer)
                {
                    var used = await db.CouponRedemptions
                        .AsNoTracking()
                        .CountAsync(r => r.CouponId == coupon.Id && r.CustomerId == cid, ct);

                    if (used >= perCustomer)
                    {
                        return Result<CouponValidation>.Failure("You have already used this discount code.");
                    }
                }

                if (coupon.FirstOrderOnly)
                {
                    var hasOrdered = await db.Orders
                        .AsNoTracking()
                        .AnyAsync(o => o.CustomerId == cid && o.Status != OrderStatus.Cancelled, ct);

                    if (hasOrdered)
                    {
                        return Result<CouponValidation>.Failure("This code is for first orders only.");
                    }
                }
            }
        }
        else if (coupon.FirstOrderOnly || coupon.UsageLimitPerCustomer is not null)
        {
            // A per-customer limit cannot be enforced against an anonymous shopper, so the coupon
            // is refused rather than handed out without its restriction.
            return Result<CouponValidation>.Failure("Sign in to use this discount code.");
        }

        var discount = CalculateDiscount(coupon.DiscountType, coupon.Value, coupon.MaxDiscountAmount, subtotal);

        return Result<CouponValidation>.Success(new CouponValidation(
            coupon.Id,
            coupon.Code,
            coupon.DiscountType,
            coupon.Value,
            discount,
            coupon.DiscountType == DiscountType.FreeShipping));
    }

    /// <summary>
    /// Works out the money discounted. Pure and total, so it is trivially testable and cannot
    /// produce a negative total or exceed the basket.
    /// </summary>
    internal static decimal CalculateDiscount(
        DiscountType type, decimal value, decimal? maxDiscount, decimal subtotal)
    {
        var discount = type switch
        {
            DiscountType.Percentage => subtotal * (value / 100m),
            DiscountType.FixedAmount => value,

            // Free shipping discounts the shipping line, not the goods; the checkout applies it
            // when pricing delivery.
            DiscountType.FreeShipping => 0m,
            _ => 0m
        };

        if (maxDiscount is { } cap && discount > cap)
        {
            discount = cap;
        }

        // Never discount more than the basket is worth — that would make the order total negative.
        discount = Math.Min(discount, subtotal);

        return Math.Round(Math.Max(0m, discount), 2, MidpointRounding.AwayFromZero);
    }
}

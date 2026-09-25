using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Carts;
using Store.Domain.Enums;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public interface ICartService
{
    Task<CartDto> GetAsync(CancellationToken ct = default);
    Task<Result<CartDto>> AddAsync(AddToCartRequest request, CancellationToken ct = default);
    Task<Result<CartDto>> UpdateQuantityAsync(Guid itemId, int quantity, CancellationToken ct = default);
    Task<Result<CartDto>> RemoveAsync(Guid itemId, CancellationToken ct = default);
    Task<Result<CartDto>> ClearAsync(CancellationToken ct = default);
    Task<Result<CartDto>> ApplyCouponAsync(string code, CancellationToken ct = default);
    Task<Result<CartDto>> RemoveCouponAsync(CancellationToken ct = default);

    /// <summary>Folds a guest cart into the signed-in customer's cart. Called on login.</summary>
    Task MergeGuestCartAsync(string anonymousId, Guid customerId, CancellationToken ct = default);
}

/// <summary>
/// The shopping cart.
/// </summary>
/// <remarks>
/// Prices are always recalculated from the live variant, never trusted from the client or from the
/// stored snapshot — a cart is user-controlled input, and letting it dictate price is how stores
/// get robbed. The snapshot exists only so the UI can say "this got cheaper while you shopped".
/// </remarks>
public sealed class CartService(
    StoreDbContext db,
    ICurrentUser currentUser,
    IDiscountService discounts,
    IDateTimeProvider clock,
    ILogger<CartService> logger) : ICartService
{
    /// <summary>Caps a single line. Stops a typo (or a script) reserving the entire shelf.</summary>
    private const int MaxQuantityPerLine = 99;

    public async Task<CartDto> GetAsync(CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);
        return cart is null ? EmptyCart() : await BuildDtoAsync(cart, ct);
    }

    public async Task<Result<CartDto>> AddAsync(AddToCartRequest request, CancellationToken ct = default)
    {
        if (request.Quantity < 1)
        {
            return Result<CartDto>.Failure("Quantity must be at least 1.");
        }

        var variant = await db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId && v.IsActive, ct);

        if (variant is null || variant.Product.Status != ProductStatus.Active)
        {
            return Result<CartDto>.NotFound("That product is no longer available.");
        }

        var cart = await LoadAsync(createIfMissing: true, ct) ?? throw new InvalidOperationException("Cart could not be created.");

        var existing = cart.Items.FirstOrDefault(i => i.ProductVariantId == request.ProductVariantId);
        var desired = (existing?.Quantity ?? 0) + request.Quantity;

        if (desired > MaxQuantityPerLine)
        {
            return Result<CartDto>.Failure($"You can order at most {MaxQuantityPerLine} of a single item.");
        }

        // Availability is checked against sellable stock (on hand minus reserved), so two shoppers
        // cannot both be told the last unit is theirs.
        if (variant.TrackInventory && !variant.AllowBackorder && desired > variant.AvailableQuantity)
        {
            return variant.AvailableQuantity == 0
                ? Result<CartDto>.Failure($"{variant.Product.Name} is out of stock.")
                : Result<CartDto>.Failure($"Only {variant.AvailableQuantity} left in stock.");
        }

        if (existing is not null)
        {
            existing.Quantity = desired;
            existing.UpdatedAt = clock.UtcNow;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                CartId = cart.Id,
                ProductVariantId = variant.Id,
                Quantity = request.Quantity,
                UnitPriceSnapshot = variant.Price,
                AddedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
        }

        cart.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    public async Task<Result<CartDto>> UpdateQuantityAsync(Guid itemId, int quantity, CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);
        var item = cart?.Items.FirstOrDefault(i => i.Id == itemId);

        if (cart is null || item is null)
        {
            return Result<CartDto>.NotFound("That item is not in your cart.");
        }

        if (quantity < 1)
        {
            return await RemoveAsync(itemId, ct);
        }

        if (quantity > MaxQuantityPerLine)
        {
            return Result<CartDto>.Failure($"You can order at most {MaxQuantityPerLine} of a single item.");
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == item.ProductVariantId, ct);

        if (variant is null)
        {
            return Result<CartDto>.NotFound("That product is no longer available.");
        }

        if (variant.TrackInventory && !variant.AllowBackorder && quantity > variant.AvailableQuantity)
        {
            return Result<CartDto>.Failure($"Only {variant.AvailableQuantity} left in stock.");
        }

        item.Quantity = quantity;
        item.UpdatedAt = clock.UtcNow;
        cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    public async Task<Result<CartDto>> RemoveAsync(Guid itemId, CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);
        var item = cart?.Items.FirstOrDefault(i => i.Id == itemId);

        if (cart is null || item is null)
        {
            return Result<CartDto>.NotFound("That item is not in your cart.");
        }

        db.CartItems.Remove(item);
        cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    public async Task<Result<CartDto>> ClearAsync(CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);

        if (cart is null)
        {
            return Result<CartDto>.Success(EmptyCart());
        }

        db.CartItems.RemoveRange(cart.Items);
        cart.CouponId = null;
        cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    public async Task<Result<CartDto>> ApplyCouponAsync(string code, CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);

        if (cart is null || cart.Items.Count == 0)
        {
            return Result<CartDto>.Failure("Add something to your cart before applying a code.");
        }

        var subtotal = cart.Items.Sum(i => i.ProductVariant.Price * i.Quantity);

        // Validated fully here, not just looked up, so an invalid code is rejected at the moment
        // it is typed rather than silently ignored at checkout.
        var validation = await discounts.ValidateAsync(code, subtotal, currentUser.UserId, ct);

        if (!validation.Succeeded)
        {
            return Result<CartDto>.Failure(validation.Error!);
        }

        cart.CouponId = validation.Required.CouponId;
        cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    public async Task<Result<CartDto>> RemoveCouponAsync(CancellationToken ct = default)
    {
        var cart = await LoadAsync(createIfMissing: false, ct);

        if (cart is null)
        {
            return Result<CartDto>.Success(EmptyCart());
        }

        cart.CouponId = null;
        cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<CartDto>.Success(await BuildDtoAsync(await ReloadAsync(cart.Id, ct), ct));
    }

    /// <summary>
    /// Merges a guest cart into the customer's cart on sign-in.
    /// </summary>
    /// <remarks>
    /// Quantities are summed rather than overwritten, and the guest cart is then deleted. Replacing
    /// the customer's cart — or ignoring the guest cart — silently loses items the shopper chose,
    /// which is a reliable way to lose the sale.
    /// </remarks>
    public async Task MergeGuestCartAsync(string anonymousId, Guid customerId, CancellationToken ct = default)
    {
        var guestCart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.AnonymousId == anonymousId, ct);

        if (guestCart is null || guestCart.Items.Count == 0)
        {
            return;
        }

        var customerCart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (customerCart is null)
        {
            // No existing cart: simply reassign the guest cart rather than copying it.
            guestCart.CustomerId = customerId;
            guestCart.AnonymousId = null;
            guestCart.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Adopted guest cart {CartId} for customer {CustomerId}", guestCart.Id, customerId);
            return;
        }

        foreach (var guestItem in guestCart.Items)
        {
            var existing = customerCart.Items.FirstOrDefault(i => i.ProductVariantId == guestItem.ProductVariantId);

            if (existing is not null)
            {
                existing.Quantity = Math.Min(MaxQuantityPerLine, existing.Quantity + guestItem.Quantity);
                existing.UpdatedAt = clock.UtcNow;
            }
            else
            {
                customerCart.Items.Add(new CartItem
                {
                    CartId = customerCart.Id,
                    ProductVariantId = guestItem.ProductVariantId,
                    Quantity = guestItem.Quantity,
                    UnitPriceSnapshot = guestItem.UnitPriceSnapshot,
                    AddedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }
        }

        customerCart.UpdatedAt = clock.UtcNow;
        db.Carts.Remove(guestCart);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Merged guest cart into customer cart {CartId}", customerCart.Id);
    }

    // ==========================================================================================
    // Internals
    // ==========================================================================================

    /// <summary>
    /// Resolves the caller's cart: the signed-in customer's, or the guest cart identified by the
    /// anonymous cookie.
    /// </summary>
    private async Task<Cart?> LoadAsync(bool createIfMissing, CancellationToken ct)
    {
        var customerId = await ResolveCustomerIdAsync(ct);
        var anonymousId = currentUser.AnonymousId;

        if (customerId is null && string.IsNullOrWhiteSpace(anonymousId))
        {
            return null;
        }

        var cart = await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => customerId != null
                ? c.CustomerId == customerId
                : c.AnonymousId == anonymousId, ct);

        if (cart is not null || !createIfMissing)
        {
            return cart;
        }

        cart = new Cart
        {
            CustomerId = customerId,
            AnonymousId = customerId is null ? anonymousId : null,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(30)
        };

        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);

        return cart;
    }

    private async Task<Cart> ReloadAsync(Guid cartId, CancellationToken ct) =>
        await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .Include(c => c.Coupon)
            .FirstAsync(c => c.Id == cartId, ct);

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

    private async Task<CartDto> BuildDtoAsync(Cart cart, CancellationToken ct)
    {
        var variantIds = cart.Items.Select(i => i.ProductVariantId).ToList();

        // One query for every line's image, rather than one per line.
        var images = await db.ProductImages
            .AsNoTracking()
            .Where(i => cart.Items.Select(ci => ci.ProductVariant.ProductId).Contains(i.ProductId))
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
            .Select(i => new { i.ProductId, i.ThumbnailUrl, i.Url })
            .ToListAsync(ct);

        var imageByProduct = images
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var warnings = new List<string>();
        var items = new List<CartItemDto>(cart.Items.Count);

        foreach (var item in cart.Items.OrderBy(i => i.AddedAt))
        {
            var variant = item.ProductVariant;
            var product = variant.Product;

            // Live price, never the snapshot — the snapshot is only used to detect a change.
            var unitPrice = variant.Price;
            var available = variant.AvailableQuantity;
            var isAvailable = variant.IsSellable && (!variant.TrackInventory || variant.AllowBackorder || available >= item.Quantity);

            if (!isAvailable)
            {
                warnings.Add(available == 0
                    ? $"{product.Name} is now out of stock."
                    : $"Only {available} of {product.Name} remain — your quantity was reduced.");
            }

            if (item.UnitPriceSnapshot > 0 && item.UnitPriceSnapshot != unitPrice)
            {
                warnings.Add(unitPrice < item.UnitPriceSnapshot
                    ? $"{product.Name} is now cheaper."
                    : $"The price of {product.Name} has changed.");
            }

            imageByProduct.TryGetValue(variant.ProductId, out var image);

            items.Add(new CartItemDto(
                item.Id,
                variant.Id,
                variant.ProductId,
                product.Name,
                product.Slug,
                variant.Name,
                variant.Sku,
                image?.ThumbnailUrl ?? image?.Url,
                unitPrice,
                variant.CompareAtPrice,
                item.Quantity,
                unitPrice * item.Quantity,
                available,
                isAvailable,
                variant.Unit,
                variant.UnitValue,
                item.UnitPriceSnapshot));
        }

        var subtotal = items.Sum(i => i.LineTotal);

        AppliedCouponDto? coupon = null;
        var discountTotal = 0m;

        if (cart.Coupon is not null)
        {
            // Re-validated on every read: a coupon can expire, hit its limit, or stop meeting the
            // minimum spend while it sits on the cart.
            var validation = await discounts.ValidateAsync(cart.Coupon.Code, subtotal, currentUser.UserId, ct);

            if (validation.Succeeded)
            {
                discountTotal = validation.Required.DiscountAmount;
                coupon = new AppliedCouponDto(
                    cart.Coupon.Code,
                    cart.Coupon.Description,
                    cart.Coupon.DiscountType,
                    cart.Coupon.Value,
                    discountTotal);
            }
            else
            {
                warnings.Add($"Coupon {cart.Coupon.Code} is no longer valid: {validation.Error}");
            }
        }

        var weightKg = cart.Items.Sum(i => (i.ProductVariant.WeightGrams ?? 0) * i.Quantity) / 1000m;

        var totals = new CartTotalsDto(
            items.Sum(i => i.Quantity),
            items.Count,
            subtotal,
            discountTotal,
            Math.Max(0, subtotal - discountTotal),
            weightKg,
            "HKD");

        return new CartDto(cart.Id, items, totals, coupon, warnings);
    }

    private static CartDto EmptyCart() =>
        new(Guid.Empty, [], new CartTotalsDto(0, 0, 0, 0, 0, 0, "HKD"), null, []);
}

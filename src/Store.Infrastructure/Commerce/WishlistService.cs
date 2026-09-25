using Microsoft.EntityFrameworkCore;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Customers;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public interface IWishlistService
{
    Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken ct = default);
    Task<Result> AddAsync(AddToWishlistRequest request, CancellationToken ct = default);
    Task<Result> RemoveAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Adds or removes in one call — what a heart icon actually needs.</summary>
    Task<Result<bool>> ToggleAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Ids the signed-in customer has saved, for rendering hearts across a product grid.</summary>
    Task<IReadOnlySet<Guid>> GetProductIdsAsync(CancellationToken ct = default);
}

/// <summary>
/// Saved products. Absent from the legacy store entirely.
/// </summary>
/// <remarks>
/// Keyed on product rather than variant: shoppers save "this item", then pick a pack size when
/// they add it to the cart. The price at save time is recorded so the UI can surface a genuine
/// "this is cheaper now" prompt, which is the main reason a wishlist earns its keep.
/// </remarks>
public sealed class WishlistService(
    StoreDbContext db,
    ICurrentUser currentUser,
    ICustomerContext customers,
    IDateTimeProvider clock) : IWishlistService
{
    public async Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken ct = default)
    {
        if (await customers.GetIdAsync(ct) is not { } customerId)
        {
            return [];
        }

        return await db.WishlistItems
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WishlistItemDto(
                w.Id,
                w.ProductId,
                w.Product.Name,
                w.Product.Slug,
                w.Product.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => i.Url).FirstOrDefault(),
                w.Product.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ThumbnailUrl).FirstOrDefault(),
                w.Product.MinPrice,
                w.Product.MaxCompareAtPrice,
                w.Product.InStock,
                w.Product.RatingAverage,
                w.Product.RatingCount,
                w.PriceWhenAdded,
                w.NotifyOnRestock,
                w.NotifyOnPriceDrop,
                w.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Result> AddAsync(AddToWishlistRequest request, CancellationToken ct = default)
    {
        if (await customers.GetOrCreateIdAsync(ct) is not { } customerId)
        {
            return Result.Forbidden(NoProfileMessage("save items"));
        }

        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.Id, p.MinPrice })
            .FirstOrDefaultAsync(ct);

        if (product is null)
        {
            return Result.NotFound("Product not found.");
        }

        var existing = await db.WishlistItems
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == request.ProductId, ct);

        if (existing is not null)
        {
            // Already saved — update the alert preferences rather than failing. Saving twice is a
            // reasonable thing for a shopper to do and should not be an error.
            existing.NotifyOnRestock = request.NotifyOnRestock;
            existing.NotifyOnPriceDrop = request.NotifyOnPriceDrop;
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }

        db.WishlistItems.Add(new WishlistItem
        {
            CustomerId = customerId,
            ProductId = request.ProductId,
            PriceWhenAdded = product.MinPrice,
            NotifyOnRestock = request.NotifyOnRestock,
            NotifyOnPriceDrop = request.NotifyOnPriceDrop,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(Guid productId, CancellationToken ct = default)
    {
        if (await customers.GetOrCreateIdAsync(ct) is not { } customerId)
        {
            return Result.Forbidden(NoProfileMessage("manage saved items"));
        }

        await db.WishlistItems
            .Where(w => w.CustomerId == customerId && w.ProductId == productId)
            .ExecuteDeleteAsync(ct);

        return Result.Success();
    }

    public async Task<Result<bool>> ToggleAsync(Guid productId, CancellationToken ct = default)
    {
        if (await customers.GetOrCreateIdAsync(ct) is not { } customerId)
        {
            return Result<bool>.Forbidden(NoProfileMessage("save items"));
        }

        var existing = await db.WishlistItems
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId, ct);

        if (existing is not null)
        {
            db.WishlistItems.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(false);
        }

        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.MinPrice })
            .FirstOrDefaultAsync(ct);

        if (product is null)
        {
            return Result<bool>.NotFound("Product not found.");
        }

        db.WishlistItems.Add(new WishlistItem
        {
            CustomerId = customerId,
            ProductId = productId,
            PriceWhenAdded = product.MinPrice,
            NotifyOnRestock = true,
            NotifyOnPriceDrop = true,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Returns just the saved product ids.
    /// </summary>
    /// <remarks>
    /// The storefront calls this once per page so every card can render its heart in the right
    /// state — far cheaper than asking "is this saved?" per product.
    /// </remarks>
    public async Task<IReadOnlySet<Guid>> GetProductIdsAsync(CancellationToken ct = default)
    {
        if (await customers.GetIdAsync(ct) is not { } customerId)
        {
            return new HashSet<Guid>();
        }

        var ids = await db.WishlistItems
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .Select(w => w.ProductId)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    /// <summary>
    /// The refusal for a caller with no storefront profile.
    /// </summary>
    /// <remarks>
    /// A missing profile means one of two quite different things, and telling a signed-in staff
    /// member to "sign in" is the kind of message that sends someone looking for a bug in the
    /// login form. <see cref="Customer"/> is deliberately separate from the identity user so that
    /// staff accounts carry no commerce columns — so a staff-only account genuinely has nowhere to
    /// save an item to, and should be told that rather than told to do something it has already
    /// done.
    /// </remarks>
    private string NoProfileMessage(string action) =>
        currentUser.IsAuthenticated
            ? $"This account has no storefront profile, so it cannot {action}. Staff accounts are not shopper accounts — sign in with a customer account instead."
            : $"Sign in to {action}.";
}

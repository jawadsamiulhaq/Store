using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Application.Reviews;
using Store.Domain.Enums;
using Store.Domain.Reviews;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Reviews;

public interface IReviewService
{
    Task<ProductReviewsDto> GetForProductAsync(
        Guid productId, int page, int pageSize, ReviewSort sort, CancellationToken ct = default);

    Task<Result<ReviewDto>> SubmitAsync(SubmitReviewRequest request, CancellationToken ct = default);
    Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken ct = default);

    // ---- Admin ----
    Task<PagedResult<AdminReviewDto>> ListAsync(ReviewQuery query, CancellationToken ct = default);
    Task<Result> ModerateAsync(Guid id, ModerateReviewRequest request, CancellationToken ct = default);
    Task<Result> ReplyAsync(Guid id, string reply, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Product reviews — absent from the legacy store entirely, which left the catalogue with no
/// social proof at all.
/// </summary>
/// <remarks>
/// Reviews are moderated: they arrive <see cref="ReviewStatus.Pending"/> and only an approved
/// review contributes to the product's rating. Approval (and un-approval) is the single place the
/// denormalised <c>RatingAverage</c>/<c>RatingCount</c> columns are recomputed, which is what lets
/// the catalogue grid show stars without aggregating the Reviews table on every request.
/// </remarks>
public sealed class ReviewService(
    StoreDbContext db,
    ICurrentUser currentUser,
    ICacheService cache,
    IDateTimeProvider clock,
    ILogger<ReviewService> logger) : IReviewService
{
    public async Task<ProductReviewsDto> GetForProductAsync(
        Guid productId, int page, int pageSize, ReviewSort sort, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var approved = db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved);

        // The whole histogram in one grouped query, not five counts.
        var buckets = await approved
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = buckets.Sum(b => b.Count);

        var summary = new RatingSummaryDto(
            total == 0 ? 0 : Math.Round(buckets.Sum(b => b.Rating * (double)b.Count) / total, 2),
            total,
            buckets.FirstOrDefault(b => b.Rating == 5)?.Count ?? 0,
            buckets.FirstOrDefault(b => b.Rating == 4)?.Count ?? 0,
            buckets.FirstOrDefault(b => b.Rating == 3)?.Count ?? 0,
            buckets.FirstOrDefault(b => b.Rating == 2)?.Count ?? 0,
            buckets.FirstOrDefault(b => b.Rating == 1)?.Count ?? 0);

        var sorted = sort switch
        {
            ReviewSort.Newest => approved.OrderByDescending(r => r.CreatedAt),
            ReviewSort.HighestRating => approved.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            ReviewSort.LowestRating => approved.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),

            // Default: the reviews other shoppers found useful, then recency. Verified purchases
            // outrank unverified ones at equal helpfulness.
            _ => approved
                .OrderByDescending(r => r.HelpfulCount)
                .ThenByDescending(r => r.IsVerifiedPurchase)
                .ThenByDescending(r => r.CreatedAt)
        };

        var items = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewDto(
                r.Id,
                r.ProductId,
                // Surname reduced to an initial. A public review should not publish a full name.
                r.Customer.User.FirstName + " " +
                    (r.Customer.User.LastName.Length > 0 ? r.Customer.User.LastName.Substring(0, 1) + "." : ""),
                r.Customer.User.AvatarUrl,
                r.Rating,
                r.Title,
                r.Body,
                r.IsVerifiedPurchase,
                r.HelpfulCount,
                r.AdminReply,
                r.AdminRepliedAt,
                r.CreatedAt))
            .ToListAsync(ct);

        var (canReview, reason) = await EvaluateEligibilityAsync(productId, ct);

        return new ProductReviewsDto(
            summary,
            new PagedResult<ReviewDto>(items, page, pageSize, total),
            canReview,
            reason);
    }

    public async Task<Result<ReviewDto>> SubmitAsync(
        SubmitReviewRequest request, CancellationToken ct = default)
    {
        if (request.Rating is < 1 or > 5)
        {
            return Result<ReviewDto>.Failure("Choose a rating between 1 and 5 stars.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Result<ReviewDto>.Failure("Write a few words about the product.");
        }

        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return Result<ReviewDto>.Forbidden("Sign in to leave a review.");
        }

        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
        {
            return Result<ReviewDto>.NotFound("Product not found.");
        }

        if (await db.Reviews.AnyAsync(r => r.CustomerId == customerId && r.ProductId == request.ProductId, ct))
        {
            return Result<ReviewDto>.Conflict("You have already reviewed this product.");
        }

        // Verified-purchase check: has this customer actually bought the item on an order that was
        // not cancelled? This is what backs the badge, and the badge is what makes reviews
        // persuasive rather than decorative.
        var purchase = await db.OrderItems
            .AsNoTracking()
            .Where(i => i.ProductId == request.ProductId
                        && i.Order.CustomerId == customerId
                        && i.Order.Status != OrderStatus.Cancelled)
            .Select(i => new { i.OrderId })
            .FirstOrDefaultAsync(ct);

        var requirePurchase = await GetSettingBoolAsync("catalog.reviews-require-purchase", false, ct);

        if (requirePurchase && purchase is null)
        {
            return Result<ReviewDto>.Forbidden("Only customers who bought this item can review it.");
        }

        var requireApproval = await GetSettingBoolAsync("catalog.reviews-require-approval", true, ct);

        var review = new Review
        {
            ProductId = request.ProductId,
            CustomerId = customerId,
            OrderId = purchase?.OrderId,
            IsVerifiedPurchase = purchase is not null,
            Rating = request.Rating,
            Title = request.Title?.Trim(),
            Body = request.Body.Trim(),
            Status = requireApproval ? ReviewStatus.Pending : ReviewStatus.Approved,
            CreatedAt = clock.UtcNow
        };

        db.Reviews.Add(review);

        // Mark the purchased line as reviewed so the customer is not prompted again for it.
        if (purchase is not null)
        {
            await db.OrderItems
                .Where(i => i.OrderId == purchase.OrderId && i.ProductId == request.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsReviewed, true), ct);
        }

        await db.SaveChangesAsync(ct);

        if (review.Status == ReviewStatus.Approved)
        {
            await RecalculateProductRatingAsync(request.ProductId, ct);
        }

        logger.LogInformation(
            "Review submitted for product {ProductId} by customer {CustomerId} (verified: {Verified}, status: {Status})",
            request.ProductId, customerId, review.IsVerifiedPurchase, review.Status);

        return Result<ReviewDto>.Success(new ReviewDto(
            review.Id, review.ProductId, "You", null, review.Rating, review.Title, review.Body,
            review.IsVerifiedPurchase, 0, null, null, review.CreatedAt));
    }

    public async Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken ct = default)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return Result.Forbidden("Sign in to vote on reviews.");
        }

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);

        if (review is null)
        {
            return Result.NotFound("Review not found.");
        }

        // One vote per customer per review, enforced by a unique index as well as this check, so
        // the counter cannot be inflated by repeated clicks.
        if (await db.ReviewVotes.AnyAsync(v => v.ReviewId == reviewId && v.CustomerId == customerId, ct))
        {
            return Result.Conflict("You have already marked this review as helpful.");
        }

        db.ReviewVotes.Add(new ReviewVote
        {
            ReviewId = reviewId,
            CustomerId = customerId,
            IsHelpful = true,
            CreatedAt = clock.UtcNow
        });

        review.HelpfulCount += 1;
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ==========================================================================================
    // Admin
    // ==========================================================================================

    public async Task<PagedResult<AdminReviewDto>> ListAsync(
        ReviewQuery query, CancellationToken ct = default)
    {
        var reviews = db.Reviews.AsNoTracking();

        if (query.Status is { } status) reviews = reviews.Where(r => r.Status == status);
        if (query.ProductId is { } productId) reviews = reviews.Where(r => r.ProductId == productId);
        if (query.Rating is { } rating) reviews = reviews.Where(r => r.Rating == rating);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            reviews = reviews.Where(r => r.Body.Contains(term) || r.Title!.Contains(term));
        }

        var total = await reviews.CountAsync(ct);

        var items = await reviews
            // Pending first — the moderation queue is the reason this screen exists.
            .OrderBy(r => r.Status)
            .ThenByDescending(r => r.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(r => new AdminReviewDto(
                r.Id,
                r.ProductId,
                r.Product.Name,
                r.Product.Slug,
                r.Customer.User.FirstName + " " + r.Customer.User.LastName,
                r.Customer.User.Email!,
                r.Rating,
                r.Title,
                r.Body,
                r.Status,
                r.IsVerifiedPurchase,
                r.HelpfulCount,
                r.AdminReply,
                r.RejectionReason,
                r.CreatedAt,
                r.ModeratedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminReviewDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<Result> ModerateAsync(
        Guid id, ModerateReviewRequest request, CancellationToken ct = default)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (review is null)
        {
            return Result.NotFound("Review not found.");
        }

        if (request.Status == ReviewStatus.Rejected && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Result.Failure("Give a reason when rejecting a review.");
        }

        review.Status = request.Status;
        review.RejectionReason = request.Status == ReviewStatus.Rejected ? request.RejectionReason : null;
        review.ModeratedAt = clock.UtcNow;
        review.ModeratedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);

        // Recomputed on every moderation decision, in both directions — approving adds the rating,
        // rejecting a previously approved review must remove it again.
        await RecalculateProductRatingAsync(review.ProductId, ct);

        logger.LogInformation("Review {ReviewId} moderated to {Status}", id, request.Status);
        return Result.Success();
    }

    public async Task<Result> ReplyAsync(Guid id, string reply, CancellationToken ct = default)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (review is null)
        {
            return Result.NotFound("Review not found.");
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            return Result.Failure("Write a reply before posting it.");
        }

        review.AdminReply = reply.Trim();
        review.AdminRepliedAt = clock.UtcNow;
        review.AdminRepliedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (review is null)
        {
            return Result.NotFound("Review not found.");
        }

        var productId = review.ProductId;

        db.Reviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await RecalculateProductRatingAsync(productId, ct);

        return Result.Success();
    }

    // ==========================================================================================
    // Internals
    // ==========================================================================================

    /// <summary>
    /// Recomputes the product's denormalised rating columns from its approved reviews.
    /// </summary>
    /// <remarks>
    /// The only place <c>RatingAverage</c>/<c>RatingCount</c> are written. Keeping that in one
    /// method is what stops the catalogue's stars drifting away from the reviews behind them.
    /// </remarks>
    private async Task RecalculateProductRatingAsync(Guid productId, CancellationToken ct)
    {
        var stats = await db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync(ct);

        await db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.RatingCount, stats == null ? 0 : stats.Count)
                .SetProperty(p => p.RatingAverage, stats == null ? 0d : Math.Round(stats.Average, 2)), ct);

        await cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);
    }

    /// <summary>Works out whether the caller may review this product, and why not if they cannot.</summary>
    private async Task<(bool CanReview, string? Reason)> EvaluateEligibilityAsync(
        Guid productId, CancellationToken ct)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return (false, "Sign in to leave a review.");
        }

        if (await db.Reviews.AsNoTracking().AnyAsync(r => r.CustomerId == customerId && r.ProductId == productId, ct))
        {
            return (false, "You have already reviewed this product.");
        }

        if (await GetSettingBoolAsync("catalog.reviews-require-purchase", false, ct))
        {
            var hasBought = await db.OrderItems
                .AsNoTracking()
                .AnyAsync(i => i.ProductId == productId
                               && i.Order.CustomerId == customerId
                               && i.Order.Status != OrderStatus.Cancelled, ct);

            if (!hasBought)
            {
                return (false, "Only customers who bought this item can review it.");
            }
        }

        return (true, null);
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

    private async Task<bool> GetSettingBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}

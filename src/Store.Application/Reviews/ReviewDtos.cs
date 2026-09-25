using Store.Application.Common;
using Store.Domain.Enums;

namespace Store.Application.Reviews;

public sealed record ReviewDto(
    Guid Id,
    Guid ProductId,
    string CustomerName,
    string? CustomerAvatar,
    int Rating,
    string? Title,
    string Body,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    string? AdminReply,
    DateTimeOffset? AdminRepliedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// The rating breakdown shown beside a product's reviews — average, total, and a count per star.
/// </summary>
/// <remarks>
/// Computed from approved reviews only, in a single grouped query, so the histogram never costs
/// five separate counts.
/// </remarks>
public sealed record RatingSummaryDto(
    double Average,
    int Total,
    int FiveStar,
    int FourStar,
    int ThreeStar,
    int TwoStar,
    int OneStar)
{
    public int PercentFor(int stars)
    {
        if (Total == 0)
        {
            return 0;
        }

        var count = stars switch
        {
            5 => FiveStar,
            4 => FourStar,
            3 => ThreeStar,
            2 => TwoStar,
            _ => OneStar
        };

        return (int)Math.Round(count * 100.0 / Total);
    }
}

public sealed record ProductReviewsDto(
    RatingSummaryDto Summary,
    PagedResult<ReviewDto> Reviews,
    bool CanReview,
    string? CannotReviewReason);

public sealed record SubmitReviewRequest(Guid ProductId, int Rating, string? Title, string Body);

public sealed record ModerateReviewRequest(ReviewStatus Status, string? RejectionReason);

public sealed record ReplyToReviewRequest(string Reply);

public sealed record AdminReviewDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string CustomerName,
    string CustomerEmail,
    int Rating,
    string? Title,
    string Body,
    ReviewStatus Status,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    string? AdminReply,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModeratedAt);

public sealed record ReviewQuery : PagedQuery
{
    public ReviewStatus? Status { get; init; }
    public Guid? ProductId { get; init; }
    public int? Rating { get; init; }
    public string? Search { get; init; }
}

public enum ReviewSort
{
    MostHelpful = 0,
    Newest = 1,
    HighestRating = 2,
    LowestRating = 3
}

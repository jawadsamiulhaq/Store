using Store.Domain.Catalog;
using Store.Domain.Common;
using Store.Domain.Customers;
using Store.Domain.Enums;

namespace Store.Domain.Reviews;

/// <summary>
/// A customer product review. Absent from the legacy store entirely, which left the catalogue
/// with no social proof at all.
/// </summary>
/// <remarks>
/// Reviews are moderated: they enter as <see cref="ReviewStatus.Pending"/> and only an approved
/// review contributes to <see cref="Product.RatingAverage"/>. Approval is the single place the
/// denormalised rating aggregate is recomputed.
/// </remarks>
public class Review : AuditableEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>
    /// Set when the reviewer actually bought the item. Drives the "Verified purchase" badge,
    /// which is the difference between a review that persuades and one that does not.
    /// </summary>
    public Guid? OrderId { get; set; }
    public bool IsVerifiedPurchase { get; set; }

    /// <summary>1–5, validated on write.</summary>
    public int Rating { get; set; }

    public string? Title { get; set; }
    public string Body { get; set; } = string.Empty;

    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    /// <summary>Count of other shoppers who marked this helpful. Drives the default review sort.</summary>
    public int HelpfulCount { get; set; }

    public string? AdminReply { get; set; }
    public DateTimeOffset? AdminRepliedAt { get; set; }
    public Guid? AdminRepliedBy { get; set; }

    public DateTimeOffset? ModeratedAt { get; set; }
    public Guid? ModeratedBy { get; set; }
    public string? RejectionReason { get; set; }

    public ICollection<ReviewVote> Votes { get; set; } = [];
}

/// <summary>
/// One shopper's "helpful" vote. Stored per customer, with a unique constraint, so the counter
/// cannot be inflated by repeated clicks.
/// </summary>
public class ReviewVote : BaseEntity
{
    public Guid ReviewId { get; set; }
    public Review Review { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public bool IsHelpful { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

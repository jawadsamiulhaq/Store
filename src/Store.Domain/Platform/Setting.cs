using Store.Domain.Common;
using Store.Domain.Enums;
using Store.Domain.Identity;

namespace Store.Domain.Platform;

/// <summary>
/// A store configuration value, editable by staff without a deploy. Read constantly and written
/// rarely, so the whole table is cached and invalidated on write.
/// </summary>
public class Setting : BaseEntity
{
    /// <summary>Dotted key, e.g. <c>store.name</c> or <c>checkout.min-order-amount</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    /// <summary>Grouping for the admin settings UI: Store, Checkout, Shipping, Email, Social.</summary>
    public string Group { get; set; } = "General";

    /// <summary>Hints the editor widget: string, number, boolean, json, html.</summary>
    public string DataType { get; set; } = "string";

    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Public settings are served to the storefront unauthenticated. Anything holding a
    /// credential must stay false so it is never included in that payload.
    /// </summary>
    public bool IsPublic { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Append-only record of a privileged action. Never updated or deleted — an audit trail that can
/// be edited is not an audit trail.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }

    /// <summary>Denormalised so the log stays readable after an account is removed.</summary>
    public string? UserName { get; set; }

    /// <summary>Verb: Created, Updated, Deleted, Login, LoginFailed, PermissionChanged.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    /// <summary>Human-readable label of the affected row, e.g. the product name.</summary>
    public string? EntityName { get; set; }

    /// <summary>JSON diff of changed properties only. Values of sensitive fields are redacted.</summary>
    public string? Changes { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Ties the entry back to the request that produced it, and to the structured logs.</summary>
    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An in-app notification. A null <see cref="UserId"/> means a broadcast to all users.
/// </summary>
public class Notification : BaseEntity
{
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Where clicking the notification navigates — usually an order or product.</summary>
    public string? LinkUrl { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>True once the matching email has been dispatched, so it is never sent twice.</summary>
    public bool IsEmailSent { get; set; }

    /// <summary>Targets staff notifications (low stock, new order) at holders of this permission.</summary>
    public string? RequiredPermission { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

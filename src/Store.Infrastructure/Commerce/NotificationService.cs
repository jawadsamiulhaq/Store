using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Domain.Identity;
using Store.Domain.Orders;
using Store.Domain.Platform;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    string? LinkUrl,
    string? ImageUrl,
    bool IsRead,
    DateTimeOffset CreatedAt);

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetMineAsync(int page, int pageSize, bool unreadOnly, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);

    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderStatusAsync(Order order, CancellationToken ct = default);

    /// <summary>Alerts staff holding <c>inventory.view</c> that a variant has fallen to its threshold.</summary>
    Task NotifyLowStockAsync(Guid variantId, string productName, int remaining, CancellationToken ct = default);
}

/// <summary>
/// In-app notifications, with email sent alongside where it matters.
/// </summary>
/// <remarks>
/// Notifications are written in the same request as the event that caused them rather than through
/// a queue. That keeps the system honest at this scale — an order confirmation that exists only if
/// a background worker happened to be running is worse than a few milliseconds of latency. Email
/// failures are caught and logged so a mail outage can never roll back an order.
/// </remarks>
public sealed class NotificationService(
    StoreDbContext db,
    ICurrentUser currentUser,
    IEmailSender email,
    IDateTimeProvider clock,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> GetMineAsync(
        int page, int pageSize, bool unreadOnly, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return PagedResult<NotificationDto>.Empty(page, pageSize);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Message, n.LinkUrl, n.ImageUrl, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default) =>
        currentUser.UserId is { } userId
            ? await db.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && !n.IsRead, ct)
            : 0;

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        // Scoped to the caller in the WHERE clause, so one user cannot mark another's as read.
        await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, clock.UtcNow), ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, clock.UtcNow), ct);
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var userId = await ResolveUserIdAsync(order.CustomerId, ct);

        if (userId is not null)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.OrderPlaced,
                Title = "Order received",
                Message = $"Thanks — we have your order {order.OrderNumber} and will confirm it shortly.",
                LinkUrl = $"/account/orders/{order.Id}",
                CreatedAt = clock.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }

        await SendEmailSafelyAsync(
            order.Email,
            $"Order {order.OrderNumber} received",
            $"""
             <p>Thank you for your order.</p>
             <p><strong>Order number:</strong> {order.OrderNumber}<br/>
             <strong>Total:</strong> {order.CurrencyCode} {order.GrandTotal:N2}</p>
             <p>We will send another update as soon as it is on its way.</p>
             <p>Waqas Provision Store — Ngau Chi Wan Market, Choi Hung</p>
             """,
            ct);
    }

    public async Task NotifyOrderStatusAsync(Order order, CancellationToken ct = default)
    {
        var (title, message) = order.Status switch
        {
            OrderStatus.Confirmed => ("Order confirmed", $"Order {order.OrderNumber} is confirmed and being prepared."),
            OrderStatus.Processing => ("Order being prepared", $"We are packing order {order.OrderNumber} now."),
            OrderStatus.Shipped => ("Order on its way", $"Order {order.OrderNumber} has left the shop."),
            OrderStatus.Delivered => ("Order delivered", $"Order {order.OrderNumber} has been delivered. Enjoy!"),
            OrderStatus.Cancelled => ("Order cancelled", $"Order {order.OrderNumber} has been cancelled."),
            OrderStatus.Refunded => ("Order refunded", $"Order {order.OrderNumber} has been refunded."),
            _ => ("Order updated", $"Order {order.OrderNumber} has been updated.")
        };

        var userId = await ResolveUserIdAsync(order.CustomerId, ct);

        if (userId is not null)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.OrderStatusChanged,
                Title = title,
                Message = message,
                LinkUrl = $"/account/orders/{order.Id}",
                CreatedAt = clock.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }

        await SendEmailSafelyAsync(
            order.Email,
            $"{title} — {order.OrderNumber}",
            $"<p>{message}</p><p>Waqas Provision Store</p>",
            ct);
    }

    /// <summary>
    /// Raises a low-stock alert for staff.
    /// </summary>
    /// <remarks>
    /// Targeted by <c>RequiredPermission</c> rather than by user id, so the alert reaches whoever
    /// currently holds inventory access — no list of recipients to maintain as staff change.
    /// Deduplicated within 24 hours so a popular item does not generate an alert per sale.
    /// </remarks>
    public async Task NotifyLowStockAsync(
        Guid variantId, string productName, int remaining, CancellationToken ct = default)
    {
        var since = clock.UtcNow.AddHours(-24);
        var link = $"/admin/inventory?variant={variantId}";

        var alreadyAlerted = await db.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.Type == NotificationType.LowStock
                           && n.LinkUrl == link
                           && n.CreatedAt >= since, ct);

        if (alreadyAlerted)
        {
            return;
        }

        db.Notifications.Add(new Notification
        {
            UserId = null,
            Type = NotificationType.LowStock,
            Title = "Low stock",
            Message = remaining == 0
                ? $"{productName} is out of stock."
                : $"{productName} is down to {remaining} in stock.",
            LinkUrl = link,
            RequiredPermission = Permissions.Inventory.View,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Low-stock alert raised for {Product} ({Remaining} left)", productName, remaining);
    }

    private async Task<Guid?> ResolveUserIdAsync(Guid? customerId, CancellationToken ct) =>
        customerId is null
            ? null
            : await db.Customers
                .AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => (Guid?)c.UserId)
                .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Sends mail without ever letting a mail failure break the caller. An order must not roll back
    /// because an SMTP server was unreachable.
    /// </summary>
    private async Task SendEmailSafelyAsync(string to, string subject, string body, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification email to {To}: {Subject}", to, subject);
        }
    }
}

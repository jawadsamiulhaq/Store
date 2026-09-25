using System.Linq.Expressions;
using Store.Application.Commerce;
using Store.Domain.Enums;
using Store.Domain.Orders;

namespace Store.Infrastructure.Commerce;

/// <summary>
/// Shared order projections.
/// </summary>
/// <remarks>
/// Defined once and reused by checkout, the customer's order history and the admin order screen,
/// so all three return an identical shape. Repeating a projection this size inline is how the
/// customer view and the admin view quietly drift apart.
/// </remarks>
internal static class OrderProjections
{
    /// <summary>Full order detail, evaluated in SQL. Collections are projected, never <c>Include</c>d.</summary>
    public static readonly Expression<Func<Order, OrderDetailDto>> Detail =
        o => new OrderDetailDto(
            o.Id,
            o.OrderNumber,
            o.Status,
            o.PaymentStatus,
            o.FulfillmentStatus,
            o.Email,
            o.Phone,
            new OrderAddressDto(
                o.ShippingAddress.FullName, o.ShippingAddress.Phone, o.ShippingAddress.Line1,
                o.ShippingAddress.Line2, o.ShippingAddress.District, o.ShippingAddress.City,
                o.ShippingAddress.Region, o.ShippingAddress.PostalCode, o.ShippingAddress.CountryCode),
            new OrderAddressDto(
                o.BillingAddress.FullName, o.BillingAddress.Phone, o.BillingAddress.Line1,
                o.BillingAddress.Line2, o.BillingAddress.District, o.BillingAddress.City,
                o.BillingAddress.Region, o.BillingAddress.PostalCode, o.BillingAddress.CountryCode),
            o.Subtotal,
            o.DiscountTotal,
            o.ShippingTotal,
            o.TaxTotal,
            o.GrandTotal,
            o.CurrencyCode,
            o.CouponCode,
            o.ShippingMethodName,
            o.PaymentMethod,
            o.CustomerNote,
            o.PlacedAt,
            o.ConfirmedAt,
            o.ShippedAt,
            o.DeliveredAt,
            o.CancelledAt,
            o.CancelReason,
            o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed,
            o.Items.Select(i => new OrderItemDto(
                i.Id, i.ProductId, i.ProductVariantId, i.ProductName, i.VariantName, i.Sku,
                i.ImageUrl, i.ProductSlug, i.UnitPrice, i.Quantity, i.LineDiscount, i.LineTotal,
                i.IsReviewed)).ToList(),
            // Only customer-visible transitions. Internal notes stay internal.
            o.StatusHistory
                .Where(h => h.IsCustomerVisible)
                .OrderBy(h => h.CreatedAt)
                .Select(h => new OrderTimelineEntryDto(h.ToStatus, h.Note, h.CreatedAt))
                .ToList(),
            o.Shipments
                .OrderBy(s => s.CreatedAt)
                .Select(s => new ShipmentDto(
                    s.Id, s.Carrier, s.TrackingNumber, s.TrackingUrl, s.Status,
                    s.ShippedAt, s.EstimatedDeliveryAt, s.DeliveredAt))
                .ToList());

    /// <summary>Compact row for the customer's order-history list.</summary>
    public static readonly Expression<Func<Order, OrderSummaryDto>> Summary =
        o => new OrderSummaryDto(
            o.Id,
            o.OrderNumber,
            o.Status,
            o.PaymentStatus,
            o.FulfillmentStatus,
            o.GrandTotal,
            o.CurrencyCode,
            o.Items.Sum(i => i.Quantity),
            o.Items.OrderBy(i => i.Id).Select(i => i.ImageUrl).FirstOrDefault(),
            o.PlacedAt);

    /// <summary>Compact row for the admin order queue.</summary>
    public static readonly Expression<Func<Order, AdminOrderListItemDto>> AdminListItem =
        o => new AdminOrderListItemDto(
            o.Id,
            o.OrderNumber,
            o.ShippingAddress.FullName,
            o.Email,
            o.Status,
            o.PaymentStatus,
            o.FulfillmentStatus,
            o.GrandTotal,
            o.CurrencyCode,
            o.Items.Sum(i => i.Quantity),
            o.PlacedAt);
}

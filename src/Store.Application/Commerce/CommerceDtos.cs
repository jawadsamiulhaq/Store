using Store.Application.Common;
using Store.Domain.Enums;

namespace Store.Application.Commerce;

// ============================================================================================
// Cart
// ============================================================================================

public sealed record CartDto(
    Guid Id,
    IReadOnlyList<CartItemDto> Items,
    CartTotalsDto Totals,
    AppliedCouponDto? Coupon,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A cart line. Carries live stock and price alongside the snapshot taken when it was added, so
/// the UI can tell the shopper what changed rather than silently altering their basket.
/// </summary>
public sealed record CartItemDto(
    Guid Id,
    Guid ProductVariantId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? VariantName,
    string Sku,
    string? ImageUrl,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    int Quantity,
    decimal LineTotal,
    int AvailableQuantity,
    bool IsAvailable,
    string Unit,
    decimal? UnitValue,
    decimal PriceWhenAdded)
{
    /// <summary>True when the price moved while the item sat in the cart.</summary>
    public bool PriceChanged => PriceWhenAdded > 0 && PriceWhenAdded != UnitPrice;
}

public sealed record CartTotalsDto(
    int ItemCount,
    int UniqueItemCount,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total,
    decimal TotalWeightKg,
    string CurrencyCode);

public sealed record AppliedCouponDto(
    string Code,
    string? Description,
    DiscountType DiscountType,
    decimal Value,
    decimal DiscountAmount);

public sealed record AddToCartRequest(Guid ProductVariantId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record ApplyCouponRequest(string Code);

// ============================================================================================
// Wishlist
// ============================================================================================

public sealed record WishlistItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ImageUrl,
    string? ThumbnailUrl,
    decimal MinPrice,
    decimal? CompareAtPrice,
    bool InStock,
    double RatingAverage,
    int RatingCount,
    decimal PriceWhenAdded,
    bool NotifyOnRestock,
    bool NotifyOnPriceDrop,
    DateTimeOffset CreatedAt)
{
    /// <summary>Drives the "dropped by $12 since you saved it" nudge.</summary>
    public decimal? PriceDrop => PriceWhenAdded > MinPrice ? PriceWhenAdded - MinPrice : null;
}

public sealed record AddToWishlistRequest(Guid ProductId, bool NotifyOnRestock, bool NotifyOnPriceDrop);

// ============================================================================================
// Addresses
// ============================================================================================

public sealed record AddressDto(
    Guid Id,
    string? Label,
    string FullName,
    string Phone,
    string Line1,
    string? Line2,
    string? District,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

public sealed record SaveAddressRequest(
    string? Label,
    string FullName,
    string Phone,
    string Line1,
    string? Line2,
    string? District,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

// ============================================================================================
// Shipping
// ============================================================================================

public sealed record ShippingQuoteDto(
    Guid MethodId,
    string Name,
    string? Description,
    decimal Rate,
    int EstimatedDaysMin,
    int EstimatedDaysMax,
    bool IsFree);

public sealed record ShippingZoneDto(
    Guid Id,
    string Name,
    string? Description,
    string CountryCodes,
    string? Regions,
    bool IsActive,
    int DisplayOrder,
    IReadOnlyList<ShippingMethodDto> Methods);

public sealed record ShippingMethodDto(
    Guid Id,
    Guid ShippingZoneId,
    string Name,
    string? Description,
    ShippingRateType RateType,
    decimal BaseRate,
    decimal? FreeOverAmount,
    decimal? RatePerKg,
    decimal? BaseWeightKg,
    int EstimatedDaysMin,
    int EstimatedDaysMax,
    bool IsActive,
    int DisplayOrder);

// ============================================================================================
// Checkout
// ============================================================================================

public sealed record CheckoutRequest(
    string Email,
    string Phone,
    OrderAddressRequest ShippingAddress,
    OrderAddressRequest? BillingAddress,
    Guid ShippingMethodId,
    PaymentMethod PaymentMethod,
    string? CouponCode,
    string? CustomerNote,

    /// <summary>Save the shipping address to the signed-in customer's address book.</summary>
    bool SaveAddress);

public sealed record OrderAddressRequest(
    string FullName,
    string Phone,
    string Line1,
    string? Line2,
    string? District,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// A priced preview of the order before it is placed, so the shopper sees the final figure —
/// including shipping and discount — before committing.
/// </summary>
public sealed record CheckoutSummaryDto(
    IReadOnlyList<CartItemDto> Items,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal GrandTotal,
    string CurrencyCode,
    AppliedCouponDto? Coupon,
    IReadOnlyList<ShippingQuoteDto> ShippingOptions,
    IReadOnlyList<string> Blockers);

// ============================================================================================
// Orders
// ============================================================================================

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    decimal GrandTotal,
    string CurrencyCode,
    int ItemCount,
    string? FirstItemImage,
    DateTimeOffset PlacedAt);

public sealed record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    string Email,
    string Phone,
    OrderAddressDto ShippingAddress,
    OrderAddressDto BillingAddress,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    string CurrencyCode,
    string? CouponCode,
    string? ShippingMethodName,
    PaymentMethod PaymentMethod,
    string? CustomerNote,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    bool IsCancellable,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderTimelineEntryDto> Timeline,
    IReadOnlyList<ShipmentDto> Shipments);

public sealed record OrderAddressDto(
    string FullName,
    string Phone,
    string Line1,
    string? Line2,
    string? District,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);

public sealed record OrderItemDto(
    Guid Id,
    Guid? ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string? VariantName,
    string Sku,
    string? ImageUrl,
    string? ProductSlug,
    decimal UnitPrice,
    int Quantity,
    decimal LineDiscount,
    decimal LineTotal,
    bool IsReviewed);

public sealed record OrderTimelineEntryDto(
    OrderStatus Status,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record ShipmentDto(
    Guid Id,
    string Carrier,
    string? TrackingNumber,
    string? TrackingUrl,
    ShipmentStatus Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? EstimatedDeliveryAt,
    DateTimeOffset? DeliveredAt);

public sealed record OrderQuery : PagedQuery
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public PaymentStatus? PaymentStatus { get; init; }
    public Guid? CustomerId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; } = true;
}

public sealed record AdminOrderListItemDto(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string Email,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    decimal GrandTotal,
    string CurrencyCode,
    int ItemCount,
    DateTimeOffset PlacedAt);

public sealed record UpdateOrderStatusRequest(OrderStatus Status, string? Note, bool NotifyCustomer);

public sealed record CancelOrderRequest(string Reason);

public sealed record CreateShipmentRequest(
    string Carrier,
    string? TrackingNumber,
    string? TrackingUrl,
    DateTimeOffset? EstimatedDeliveryAt,
    string? Note);

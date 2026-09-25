namespace Store.Domain.Enums;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

/// <summary>
/// Movements are recorded as an append-only ledger. <c>ProductVariant.StockQuantity</c> is the
/// maintained projection of this ledger, never the other way around, so stock is always explainable.
/// </summary>
public enum InventoryTransactionType
{
    /// <summary>Opening balance when a variant is created or imported.</summary>
    Initial = 0,
    /// <summary>Goods received from a supplier.</summary>
    Purchase = 1,
    /// <summary>Stock leaving on a placed order.</summary>
    Sale = 2,
    /// <summary>Manual correction by staff (stock take).</summary>
    Adjustment = 3,
    /// <summary>Customer returned goods to sellable stock.</summary>
    Return = 4,
    /// <summary>Written off: expired, broken, lost.</summary>
    Damage = 5,
    /// <summary>Held for an in-flight checkout. Reduces sellable, not on-hand.</summary>
    Reservation = 6,
    /// <summary>Reservation released after cancellation or expiry.</summary>
    ReservationRelease = 7
}

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
    Refunded = 6
}

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Failed = 5
}

public enum FulfillmentStatus
{
    Unfulfilled = 0,
    PartiallyFulfilled = 1,
    Fulfilled = 2,
    Returned = 3
}

public enum PaymentMethod
{
    CashOnDelivery = 0,
    BankTransfer = 1,
    Card = 2,
    FpsOrWallet = 3
}

public enum ShipmentStatus
{
    Pending = 0,
    InTransit = 1,
    OutForDelivery = 2,
    Delivered = 3,
    Failed = 4,
    Returned = 5
}

public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1,
    FreeShipping = 2
}

/// <summary>What a coupon is allowed to discount.</summary>
public enum CouponScope
{
    EntireOrder = 0,
    SpecificProducts = 1,
    SpecificCategories = 2
}

public enum ShippingRateType
{
    /// <summary>One price regardless of basket.</summary>
    Flat = 0,
    /// <summary>Flat, but waived above a spend threshold.</summary>
    FreeOverAmount = 1,
    /// <summary>Base price plus a per-kilogram rate.</summary>
    WeightBased = 2,
    /// <summary>Customer collects in store. Always zero.</summary>
    Pickup = 3
}

public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum NotificationType
{
    OrderPlaced = 0,
    OrderStatusChanged = 1,
    OrderShipped = 2,
    OrderDelivered = 3,
    LowStock = 4,
    ReviewSubmitted = 5,
    PriceDrop = 6,
    BackInStock = 7,
    Promotion = 8,
    System = 9
}

public enum BannerPosition
{
    HomeHero = 0,
    HomeSecondary = 1,
    CategoryTop = 2,
    CheckoutSidebar = 3
}

public enum ContentStatus
{
    Draft = 0,
    Published = 1
}

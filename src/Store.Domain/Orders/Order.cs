using Store.Domain.Catalog;
using Store.Domain.Common;
using Store.Domain.Customers;
using Store.Domain.Enums;

namespace Store.Domain.Orders;

/// <summary>
/// A placed order. Immutable in substance: line items, addresses and money are snapshots taken at
/// placement, never live references. If a product is renamed, repriced or deleted next week, this
/// order must still describe exactly what was bought and for how much.
/// </summary>
public class Order : AuditableEntity
{
    /// <summary>
    /// Human-facing reference such as <c>WPS-20260923-0041</c>. Customers quote this, so it is
    /// short, unique, and never exposes the primary key or a sequential total-order count.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Null for guest checkout, which is supported.</summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Unfulfilled;

    // ---- Snapshotted addresses (owned types, stored inline on this row) ----
    public OrderAddress ShippingAddress { get; set; } = new();
    public OrderAddress BillingAddress { get; set; } = new();

    // ---- Money. All decimal(18,2); never floating point. ----
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string CurrencyCode { get; set; } = "HKD";

    public string? CouponCode { get; set; }
    public Guid? CouponId { get; set; }

    public Guid? ShippingMethodId { get; set; }
    public string? ShippingMethodName { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;

    /// <summary>Delivery instructions left by the customer.</summary>
    public string? CustomerNote { get; set; }

    /// <summary>Internal note. Never serialised to the storefront.</summary>
    public string? AdminNote { get; set; }

    public DateTimeOffset PlacedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>
    /// Prevents two staff members concurrently transitioning the same order and losing one
    /// another's change — the classic "it said shipped, then flipped back" bug.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    public int TotalItems => Items.Sum(i => i.Quantity);

    /// <summary>An order can still be cancelled by the customer before it ships.</summary>
    public bool IsCancellable => Status is OrderStatus.Pending or OrderStatus.Confirmed;
}

/// <summary>
/// Address as captured at order time. An owned type — these columns live on the Orders row, so
/// reading an order never joins to the customer's current (possibly edited) address book.
/// </summary>
public class OrderAddress
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string? District { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "HK";
}

/// <summary>
/// A purchased line. Product name, variant, SKU, image and price are copied in, not referenced,
/// for the same reason the address is snapshotted.
/// </summary>
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Kept for reporting and reordering. Nullable: the variant may be deleted later.</summary>
    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public Guid? ProductId { get; set; }

    // ---- Snapshot ----
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ProductSlug { get; set; }

    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    /// <summary>Share of an order-level coupon apportioned to this line.</summary>
    public decimal LineDiscount { get; set; }

    /// <summary><c>UnitPrice * Quantity - LineDiscount</c>, persisted so reports never recompute it.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Set once the customer has reviewed this line, to stop duplicate review prompts.</summary>
    public bool IsReviewed { get; set; }
}

/// <summary>Immutable transition record. Backs the customer-facing tracking timeline.</summary>
public class OrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }

    public string? Note { get; set; }

    /// <summary>Null when the transition was automatic rather than staff-driven.</summary>
    public Guid? ChangedBy { get; set; }

    /// <summary>Whether the customer can see this entry in their tracking view.</summary>
    public bool IsCustomerVisible { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A physical dispatch. Separate from the order so a large grocery order can ship in parts
/// (ambient now, frozen tomorrow) with independent tracking.
/// </summary>
public class Shipment : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Courier name. The legacy store's delivery partner was Gogo Van.</summary>
    public string Carrier { get; set; } = string.Empty;

    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;

    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? EstimatedDeliveryAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A payment attempt against an order. Modelled as a collection because refunds and partial
/// captures are separate records, which is what makes the payment history auditable.
/// </summary>
public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public PaymentMethod Method { get; set; }

    /// <summary>Gateway name, or "manual" for cash and bank transfer.</summary>
    public string Provider { get; set; } = "manual";

    /// <summary>Gateway transaction id, for reconciliation.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>Negative for a refund.</summary>
    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTimeOffset? CapturedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

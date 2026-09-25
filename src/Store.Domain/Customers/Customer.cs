using Store.Domain.Common;
using Store.Domain.Identity;
using Store.Domain.Orders;

namespace Store.Domain.Customers;

/// <summary>
/// Storefront profile for a user. Separate from <see cref="AppUser"/> so that staff accounts do
/// not carry commerce columns, and so customer analytics can grow without touching identity.
/// </summary>
public class Customer : AuditableEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public string? Notes { get; set; }

    /// <summary>Marketing consent. Defaults to off — opt-in, not opt-out.</summary>
    public bool AcceptsMarketing { get; set; }

    // ---- Maintained lifetime aggregates ----
    // Written when an order reaches a paid/delivered state. The admin customer list sorts and
    // filters on these, and computing them per row would mean a subquery per customer.

    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }

    public ICollection<Address> Addresses { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<WishlistItem> WishlistItems { get; set; } = [];

    public decimal AverageOrderValue => TotalOrders > 0 ? TotalSpent / TotalOrders : 0m;
}

/// <summary>
/// A saved address in a customer's address book.
/// Orders do not reference this — they snapshot it (see <see cref="Orders.OrderAddress"/>) so that
/// editing an address later never rewrites the history of where something was delivered.
/// </summary>
public class Address : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>User-chosen label: "Home", "Office".</summary>
    public string? Label { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }

    /// <summary>District — the meaningful administrative level in Hong Kong (e.g. Wong Tai Sin).</summary>
    public string? District { get; set; }

    public string City { get; set; } = string.Empty;

    /// <summary>Region/territory: Kowloon, Hong Kong Island, New Territories.</summary>
    public string? Region { get; set; }

    /// <summary>Optional — Hong Kong has no postal codes, so this must never be mandatory.</summary>
    public string? PostalCode { get; set; }

    public string CountryCode { get; set; } = "HK";

    public bool IsDefaultShipping { get; set; }
    public bool IsDefaultBilling { get; set; }
}

/// <summary>
/// A product a customer saved for later. Absent from the legacy store entirely.
/// Keyed on product rather than variant: shoppers save "this item", then choose a size at add-to-cart.
/// </summary>
public class WishlistItem : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Catalog.Product Product { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Price when saved, so the UI can surface "dropped by $12 since you saved it".</summary>
    public decimal PriceWhenAdded { get; set; }

    /// <summary>Opt-in alert when the item returns to stock.</summary>
    public bool NotifyOnRestock { get; set; }

    /// <summary>Opt-in alert when the price falls below <see cref="PriceWhenAdded"/>.</summary>
    public bool NotifyOnPriceDrop { get; set; }
}

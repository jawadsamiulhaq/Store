using Store.Domain.Catalog;
using Store.Domain.Common;
using Store.Domain.Enums;

namespace Store.Domain.Inventory;

/// <summary>
/// Append-only stock ledger. Every movement is a row; rows are never updated or deleted.
/// </summary>
/// <remarks>
/// <see cref="ProductVariant.StockQuantity"/> is the running projection of this ledger, written in
/// the same transaction as the movement. Keeping the ledger means any stock figure can be
/// explained — "why does this say 12?" is answerable — and a discrepancy can be reconciled by
/// replaying <see cref="QuantityChange"/> rather than guessed at.
/// </remarks>
public class InventoryTransaction : BaseEntity
{
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public InventoryTransactionType Type { get; set; }

    /// <summary>Signed delta: negative for sales, damage and reservations.</summary>
    public int QuantityChange { get; set; }

    /// <summary>On-hand quantity immediately after this movement. Makes the ledger self-verifying.</summary>
    public int QuantityAfter { get; set; }

    /// <summary>Order number, purchase reference or stock-take id that caused the movement.</summary>
    public string? Reference { get; set; }

    public Guid? OrderId { get; set; }

    public string? Note { get; set; }

    /// <summary>Null for system-generated movements such as an order decrementing stock.</summary>
    public Guid? PerformedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

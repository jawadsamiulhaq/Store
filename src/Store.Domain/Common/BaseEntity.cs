namespace Store.Domain.Common;

/// <summary>
/// Base for every persisted entity.
/// </summary>
/// <remarks>
/// Ids are UUID v7: globally unique like a GUID, but monotonically ordered by creation time.
/// That ordering is what keeps clustered-index inserts append-only instead of causing page
/// splits across the table, which is a measurable write-throughput difference once order and
/// inventory-transaction volume grows.
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Tracks who created/modified a row and when.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Marks an entity that is hidden rather than physically removed. A global query filter
/// excludes these rows, so every query is safe by default and must opt out explicitly.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}

/// <summary>Convenience base for the common auditable + soft-deletable combination.</summary>
public abstract class AuditableEntity : BaseEntity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

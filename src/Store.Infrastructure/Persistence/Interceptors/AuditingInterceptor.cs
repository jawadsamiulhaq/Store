using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Store.Application.Common;
using Store.Domain.Common;
using Store.Domain.Platform;

namespace Store.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit fields and records an <see cref="AuditLog"/> row for every tracked change.
/// </summary>
/// <remarks>
/// Doing this in an interceptor rather than in each service means a new endpoint cannot forget to
/// audit, and <c>CreatedAt</c>/<c>UpdatedBy</c> can never drift out of sync with what was actually
/// written. It also converts a hard delete of an <see cref="ISoftDeletable"/> entity into a soft
/// delete, so a stray <c>Remove()</c> call cannot destroy a product row.
/// </remarks>
public sealed class AuditingInterceptor(
    IAuditContext auditContext,
    IDateTimeProvider clock) : SaveChangesInterceptor
{
    /// <summary>
    /// Entities whose changes are written to the audit trail. Deliberately not everything:
    /// auditing cart mutations or audit rows themselves would bury the signal in noise.
    /// </summary>
    private static readonly HashSet<string> AuditedEntities =
    [
        "Product", "ProductVariant", "Category", "Brand", "Coupon", "Order",
        "AppUser", "AppRole", "RolePermission", "UserPermission", "Setting",
        "ShippingMethod", "ShippingZone", "ContentPage", "Banner", "BlogPost"
    ];

    /// <summary>
    /// Property names never written to the audit trail in plain text.
    /// Matching is case-insensitive substring, so <c>PasswordHash</c> and <c>SecurityStamp</c>
    /// are both caught.
    /// </summary>
    private static readonly string[] SensitiveProperties =
    [
        "Password", "Hash", "Token", "Secret", "SecurityStamp", "ConcurrencyStamp", "RowVersion"
    ];

    /// <summary>Properties checked, in order, for a human-readable label on an audited row.</summary>
    private static readonly string[] DisplayNameCandidates =
    [
        "Name", "Title", "OrderNumber", "Code", "Email", "Key"
    ];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = clock.UtcNow;
        var userId = auditContext.UserId;
        var auditRows = new List<AuditLog>();

        // Materialised: converting a delete to a soft delete mutates entry state, and the
        // ChangeTracker cannot be modified while its own entries are being enumerated lazily.
        var entries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
        {
            // Audit rows are never themselves audited, and are added after this loop anyway.
            if (entry.Entity is AuditLog)
                continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    StampCreated(entry, now, userId);
                    break;

                case EntityState.Modified:
                    StampUpdated(entry, now, userId);
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletable soft:
                    // Turn the hard delete into a soft delete so history survives.
                    entry.State = EntityState.Modified;
                    soft.DeletedAt = now;
                    soft.DeletedBy = userId;
                    break;
            }

            var row = BuildAuditRow(entry, now, userId);
            if (row is not null)
            {
                auditRows.Add(row);
            }
        }

        if (auditRows.Count > 0)
        {
            // Added after the loop so these rows are not themselves audited.
            context.Set<AuditLog>().AddRange(auditRows);
        }
    }

    private static void StampCreated(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry.Entity is not IAuditable auditable)
            return;

        auditable.CreatedAt = now;
        auditable.CreatedBy ??= userId;
    }

    private static void StampUpdated(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry.Entity is not IAuditable auditable)
            return;

        auditable.UpdatedAt = now;
        auditable.UpdatedBy = userId;

        // CreatedAt/CreatedBy are write-once. Marking them unmodified means a service that
        // rehydrates a detached entity with default values cannot overwrite the real creation
        // record.
        entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
    }

    private AuditLog? BuildAuditRow(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        var entityName = entry.Entity.GetType().Name;

        if (!AuditedEntities.Contains(entityName))
            return null;

        var action = entry.State switch
        {
            EntityState.Added => "Created",
            EntityState.Modified when entry.Entity is ISoftDeletable { DeletedAt: not null } => "Deleted",
            EntityState.Modified => "Updated",
            EntityState.Deleted => "Deleted",
            _ => null
        };

        if (action is null)
            return null;

        var changes = action == "Updated" ? SerialiseChanges(entry) : null;

        // An "update" that changed nothing auditable (only a rowversion, say) is not worth a row.
        if (action == "Updated" && changes is null)
            return null;

        return new AuditLog
        {
            UserId = userId,
            UserName = auditContext.Email,
            Action = action,
            EntityType = entityName,
            EntityId = TryGetKey(entry),
            EntityName = TryGetDisplayName(entry),
            Changes = changes,
            IpAddress = auditContext.IpAddress,
            UserAgent = Truncate(auditContext.UserAgent, 500),
            CorrelationId = auditContext.CorrelationId,
            CreatedAt = now
        };
    }

    /// <summary>Serialises only the properties that actually changed, with sensitive values redacted.</summary>
    private static string? SerialiseChanges(EntityEntry entry)
    {
        var changed = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (!property.IsModified || property.Metadata.Name is nameof(IAuditable.UpdatedAt) or nameof(IAuditable.UpdatedBy))
                continue;

            var name = property.Metadata.Name;

            if (IsSensitive(name))
            {
                changed[name] = "***redacted***";
                continue;
            }

            if (Equals(property.OriginalValue, property.CurrentValue))
                continue;

            changed[name] = new { From = property.OriginalValue, To = property.CurrentValue };
        }

        if (changed.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(changed);

        // The column is nvarchar(max), but a runaway HTML description would still bloat the log.
        return json.Length > 8000 ? json[..8000] : json;
    }

    private static bool IsSensitive(string propertyName) =>
        SensitiveProperties.Any(s => propertyName.Contains(s, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetKey(EntityEntry entry) =>
        entry.Metadata.FindPrimaryKey()?.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .FirstOrDefault();

    /// <summary>Finds a human-readable label so the log reads "Product: Basmati Rice 5 kg".</summary>
    private static string? TryGetDisplayName(EntityEntry entry)
    {
        foreach (var candidate in DisplayNameCandidates)
        {
            if (entry.Metadata.FindProperty(candidate) is not null)
            {
                return Truncate(entry.Property(candidate).CurrentValue?.ToString(), 400);
            }
        }

        return null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}


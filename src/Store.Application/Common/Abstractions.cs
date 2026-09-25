namespace Store.Application.Common;

/// <summary>
/// The ambient request identity needed to stamp an audit record.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="ICurrentUser"/>, and deliberately free of any dependency
/// on the database. The auditing interceptor is constructed as part of the pooled
/// <c>DbContext</c>'s options, which are resolved from the <i>root</i> service provider — so
/// anything it depends on must be singleton-safe. Asking it for a permission check (which needs a
/// scoped <c>DbContext</c>) would be a circular dependency as well as a lifetime violation.
/// </remarks>
public interface IAuditContext
{
    Guid? UserId { get; }
    string? Email { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

/// <summary>
/// The caller behind the current request. Lets services apply ownership rules and stamp audit
/// fields without the Application layer taking a dependency on ASP.NET Core.
/// </summary>
public interface ICurrentUser : IAuditContext
{
    bool IsAuthenticated { get; }

    /// <summary>True for the unrestricted System role, which bypasses permission checks.</summary>
    bool IsSystem { get; }

    /// <summary>True for System or Admin — permission to reach the admin area at all.</summary>
    bool IsStaff { get; }

    /// <summary>Cookie-held id identifying a guest's cart before they sign in.</summary>
    string? AnonymousId { get; }

    /// <summary>
    /// Resolves the caller's effective permissions (role grants, minus user-level denials).
    /// Cached per user, so calling this repeatedly within a request is cheap.
    /// </summary>
    Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default);
}

/// <summary>Clock abstraction, so time-dependent logic (coupon windows, expiry) is testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Two-level cache facade (in-memory now, Redis-ready). Tag-based invalidation lets a single
/// product write evict every cached listing that could contain it.
/// </summary>
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByTagAsync(string tag, CancellationToken ct = default);
}

/// <summary>Cache key and tag constants, kept together so invalidation sites are greppable.</summary>
public static class CacheKeys
{
    public const string CategoryTree = "catalog:category-tree";
    public const string BrandList = "catalog:brand-list";
    public const string PublicSettings = "platform:settings:public";
    public const string ShippingMethods = "shipping:methods";
    public const string HomePage = "storefront:home";

    public static string UserPermissions(Guid userId) => $"auth:permissions:{userId}";
    public static string ProductBySlug(string slug) => $"catalog:product:{slug}";

    public static class Tags
    {
        public const string Catalog = "tag:catalog";
        public const string Categories = "tag:categories";
        public const string Brands = "tag:brands";
        public const string Products = "tag:products";
        public const string Settings = "tag:settings";
        public const string Shipping = "tag:shipping";
        public const string Permissions = "tag:permissions";
    }
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>Stores uploaded media and returns its public URL.</summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, string? folder = null, CancellationToken ct = default);
    Task DeleteAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Result of storing a file. Dimensions and derivative URLs are captured at upload time so
/// consumers can emit a correct <c>srcset</c> and reserve exact layout space.
/// </summary>
public sealed record StoredFile(
    string Url,
    string? ThumbnailUrl,
    string? WebpUrl,
    string? AvifUrl,
    string FileName,
    long SizeBytes,
    int Width,
    int Height,
    string? BlurHash);

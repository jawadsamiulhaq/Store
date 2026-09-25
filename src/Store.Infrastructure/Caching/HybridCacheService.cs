using Microsoft.Extensions.Caching.Hybrid;
using Store.Application.Common;

namespace Store.Infrastructure.Caching;

/// <summary>
/// <see cref="ICacheService"/> over <see cref="HybridCache"/>.
/// </summary>
/// <remarks>
/// <see cref="HybridCache"/> gives an in-process L1 today and an out-of-process L2 (Redis) the
/// moment a distributed cache is registered — no call-site changes. It also coalesces concurrent
/// misses for the same key into a single factory invocation, which matters on a cold start when
/// many requests hit the category tree at once (the stampede that otherwise turns one slow query
/// into a hundred).
/// </remarks>
public sealed class HybridCacheService(HybridCache cache) : ICacheService
{
    /// <summary>
    /// Default lifetime for cached reference data. Short enough that an admin edit surfaces
    /// quickly even if a tag invalidation is somehow missed, long enough to absorb traffic.
    /// </summary>
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(10);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        var options = new HybridCacheEntryOptions
        {
            Expiration = expiration ?? DefaultExpiration,

            // Keep the in-process copy slightly shorter than the distributed copy so a node that
            // missed an invalidation message self-corrects rather than serving stale data until
            // the full expiry elapses.
            LocalCacheExpiration = (expiration ?? DefaultExpiration) / 2
        };

        return await cache.GetOrCreateAsync(key, factory, options, tags, ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default) =>
        await cache.RemoveAsync(key, ct);

    /// <summary>
    /// Invalidates every entry carrying <paramref name="tag"/>. This is what lets a single product
    /// write evict all cached listings that could contain it, without tracking which keys those were.
    /// </summary>
    public async Task RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        await cache.RemoveByTagAsync(tag, ct);
}

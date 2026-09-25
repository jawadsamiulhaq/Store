using Microsoft.EntityFrameworkCore;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Enums;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

public interface IShippingService
{
    /// <summary>Prices every delivery option available for a region and basket.</summary>
    Task<IReadOnlyList<ShippingQuoteDto>> GetQuotesAsync(
        string? region, decimal subtotal, decimal weightKg, CancellationToken ct = default);

    Task<IReadOnlyList<ShippingZoneDto>> GetZonesAsync(CancellationToken ct = default);
    Task<Result<ShippingZoneDto>> SaveZoneAsync(Guid? id, ShippingZoneDto zone, CancellationToken ct = default);
    Task<Result> DeleteZoneAsync(Guid id, CancellationToken ct = default);
    Task<Result<ShippingMethodDto>> SaveMethodAsync(Guid? id, ShippingMethodDto method, CancellationToken ct = default);
    Task<Result> DeleteMethodAsync(Guid id, CancellationToken ct = default);
}

public sealed class ShippingService(StoreDbContext db, ICacheService cache) : IShippingService
{
    public async Task<IReadOnlyList<ShippingQuoteDto>> GetQuotesAsync(
        string? region, decimal subtotal, decimal weightKg, CancellationToken ct = default)
    {
        // The whole zone/method table is small and read on every checkout, so it is cached whole
        // and filtered in memory rather than queried per request.
        var zones = await cache.GetOrCreateAsync(
            CacheKeys.ShippingMethods,
            async token => await db.ShippingZones
                .AsNoTracking()
                .Where(z => z.IsActive)
                .OrderBy(z => z.DisplayOrder)
                .Select(z => new
                {
                    z.Id, z.Name, z.CountryCodes, z.Regions,
                    Methods = z.Methods
                        .Where(m => m.IsActive)
                        .OrderBy(m => m.DisplayOrder)
                        .Select(m => new
                        {
                            m.Id, m.Name, m.Description, m.RateType, m.BaseRate,
                            m.FreeOverAmount, m.RatePerKg, m.BaseWeightKg,
                            m.EstimatedDaysMin, m.EstimatedDaysMax, m.MaxWeightKg
                        })
                        .ToList()
                })
                .ToListAsync(token),
            TimeSpan.FromMinutes(30),
            [CacheKeys.Tags.Shipping],
            ct);

        // Match the customer's region; fall back to every zone so a shopper is never left with no
        // delivery option at all because their district was spelled differently.
        var matching = string.IsNullOrWhiteSpace(region)
            ? zones
            : zones.Where(z => string.IsNullOrWhiteSpace(z.Regions)
                               || z.Regions.Split(',', StringSplitOptions.TrimEntries)
                                   .Any(r => r.Equals(region, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        if (matching.Count == 0)
        {
            matching = zones;
        }

        var quotes = new List<ShippingQuoteDto>();

        foreach (var zone in matching)
        {
            foreach (var method in zone.Methods)
            {
                // Hide options the basket is too heavy for, rather than quoting a price the
                // courier will not honour.
                if (method.MaxWeightKg is { } max && weightKg > max)
                {
                    continue;
                }

                var rate = CalculateRate(
                    method.RateType, method.BaseRate, method.FreeOverAmount,
                    method.RatePerKg, method.BaseWeightKg, subtotal, weightKg);

                quotes.Add(new ShippingQuoteDto(
                    method.Id,
                    matching.Count > 1 ? $"{method.Name} — {zone.Name}" : method.Name,
                    method.Description,
                    rate,
                    method.EstimatedDaysMin,
                    method.EstimatedDaysMax,
                    rate == 0m));
            }
        }

        return [.. quotes.OrderBy(q => q.Rate).ThenBy(q => q.EstimatedDaysMin)];
    }

    /// <summary>
    /// Mirrors <see cref="Domain.Shipping.ShippingMethod.CalculateRate"/> for the cached
    /// projection, which holds plain values rather than entities.
    /// </summary>
    private static decimal CalculateRate(
        ShippingRateType type, decimal baseRate, decimal? freeOver,
        decimal? ratePerKg, decimal? baseWeight, decimal subtotal, decimal weightKg)
    {
        if (type == ShippingRateType.Pickup)
        {
            return 0m;
        }

        if (type == ShippingRateType.FreeOverAmount && freeOver is { } threshold && subtotal >= threshold)
        {
            return 0m;
        }

        if (type != ShippingRateType.WeightBased)
        {
            return baseRate;
        }

        var billable = Math.Max(0m, weightKg - (baseWeight ?? 0m));
        return baseRate + billable * (ratePerKg ?? 0m);
    }

    // ==========================================================================================
    // Admin
    // ==========================================================================================

    public async Task<IReadOnlyList<ShippingZoneDto>> GetZonesAsync(CancellationToken ct = default) =>
        await db.ShippingZones
            .AsNoTracking()
            .OrderBy(z => z.DisplayOrder)
            .Select(z => new ShippingZoneDto(
                z.Id, z.Name, z.Description, z.CountryCodes, z.Regions, z.IsActive, z.DisplayOrder,
                z.Methods.OrderBy(m => m.DisplayOrder).Select(m => new ShippingMethodDto(
                    m.Id, m.ShippingZoneId, m.Name, m.Description, m.RateType, m.BaseRate,
                    m.FreeOverAmount, m.RatePerKg, m.BaseWeightKg,
                    m.EstimatedDaysMin, m.EstimatedDaysMax, m.IsActive, m.DisplayOrder)).ToList()))
            .ToListAsync(ct);

    public async Task<Result<ShippingZoneDto>> SaveZoneAsync(
        Guid? id, ShippingZoneDto request, CancellationToken ct = default)
    {
        Domain.Shipping.ShippingZone zone;

        if (id is { } zoneId)
        {
            var existing = await db.ShippingZones.FirstOrDefaultAsync(z => z.Id == zoneId, ct);

            if (existing is null)
            {
                return Result<ShippingZoneDto>.NotFound("Shipping zone not found.");
            }

            zone = existing;
        }
        else
        {
            zone = new Domain.Shipping.ShippingZone();
            db.ShippingZones.Add(zone);
        }

        zone.Name = request.Name.Trim();
        zone.Description = request.Description;
        zone.CountryCodes = string.IsNullOrWhiteSpace(request.CountryCodes) ? "HK" : request.CountryCodes;
        zone.Regions = request.Regions;
        zone.IsActive = request.IsActive;
        zone.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Shipping, ct);

        var saved = (await GetZonesAsync(ct)).First(z => z.Id == zone.Id);
        return Result<ShippingZoneDto>.Success(saved);
    }

    public async Task<Result> DeleteZoneAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await db.ShippingZones.FirstOrDefaultAsync(z => z.Id == id, ct);

        if (zone is null)
        {
            return Result.NotFound("Shipping zone not found.");
        }

        // Orders reference the method by id for their history. Blocking the delete keeps that
        // reference meaningful; deactivating the zone is the correct way to retire it.
        var inUse = await db.Orders.AnyAsync(o => db.ShippingMethods
            .Where(m => m.ShippingZoneId == id)
            .Select(m => (Guid?)m.Id)
            .Contains(o.ShippingMethodId), ct);

        if (inUse)
        {
            return Result.Conflict(
                "This zone has been used on past orders. Deactivate it instead of deleting it.");
        }

        db.ShippingZones.Remove(zone);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Shipping, ct);

        return Result.Success();
    }

    public async Task<Result<ShippingMethodDto>> SaveMethodAsync(
        Guid? id, ShippingMethodDto request, CancellationToken ct = default)
    {
        if (!await db.ShippingZones.AnyAsync(z => z.Id == request.ShippingZoneId, ct))
        {
            return Result<ShippingMethodDto>.Failure("That shipping zone does not exist.");
        }

        Domain.Shipping.ShippingMethod method;

        if (id is { } methodId)
        {
            var existing = await db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == methodId, ct);

            if (existing is null)
            {
                return Result<ShippingMethodDto>.NotFound("Shipping method not found.");
            }

            method = existing;
        }
        else
        {
            method = new Domain.Shipping.ShippingMethod();
            db.ShippingMethods.Add(method);
        }

        method.ShippingZoneId = request.ShippingZoneId;
        method.Name = request.Name.Trim();
        method.Description = request.Description;
        method.RateType = request.RateType;
        method.BaseRate = request.BaseRate;
        method.FreeOverAmount = request.FreeOverAmount;
        method.RatePerKg = request.RatePerKg;
        method.BaseWeightKg = request.BaseWeightKg;
        method.EstimatedDaysMin = request.EstimatedDaysMin;
        method.EstimatedDaysMax = request.EstimatedDaysMax;
        method.IsActive = request.IsActive;
        method.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Shipping, ct);

        return Result<ShippingMethodDto>.Success(request with { Id = method.Id });
    }

    public async Task<Result> DeleteMethodAsync(Guid id, CancellationToken ct = default)
    {
        var method = await db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (method is null)
        {
            return Result.NotFound("Shipping method not found.");
        }

        if (await db.Orders.AnyAsync(o => o.ShippingMethodId == id, ct))
        {
            return Result.Conflict(
                "This method has been used on past orders. Deactivate it instead of deleting it.");
        }

        db.ShippingMethods.Remove(method);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Shipping, ct);

        return Result.Success();
    }
}

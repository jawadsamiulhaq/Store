using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Domain.Content;
using Store.Domain.Enums;
using Store.Domain.Promotions;
using Store.Infrastructure.Catalog;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Platform;

// ==============================================================================================
// Coupons
// ==============================================================================================

public sealed record CouponDto(
    Guid Id,
    string Code,
    string? Description,
    DiscountType DiscountType,
    decimal Value,
    decimal? MinOrderAmount,
    decimal? MaxDiscountAmount,
    CouponScope Scope,
    int? UsageLimit,
    int? UsageLimitPerCustomer,
    int UsedCount,
    bool FirstOrderOnly,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    bool IsCurrentlyValid);

public sealed record SaveCouponRequest(
    string Code,
    string? Description,
    DiscountType DiscountType,
    decimal Value,
    decimal? MinOrderAmount,
    decimal? MaxDiscountAmount,
    CouponScope Scope,
    int? UsageLimit,
    int? UsageLimitPerCustomer,
    bool FirstOrderOnly,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive);

public sealed record CouponQuery : PagedQuery
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public interface ICouponAdminService
{
    Task<PagedResult<CouponDto>> ListAsync(CouponQuery query, CancellationToken ct = default);
    Task<Result<CouponDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<CouponDto>> CreateAsync(SaveCouponRequest request, CancellationToken ct = default);
    Task<Result<CouponDto>> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class CouponAdminService(StoreDbContext db, IDateTimeProvider clock) : ICouponAdminService
{
    public async Task<PagedResult<CouponDto>> ListAsync(CouponQuery query, CancellationToken ct = default)
    {
        var coupons = db.Coupons.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            coupons = coupons.Where(c => c.Code.Contains(term) || c.Description!.Contains(term));
        }

        if (query.IsActive is { } active)
        {
            coupons = coupons.Where(c => c.IsActive == active);
        }

        var total = await coupons.CountAsync(ct);

        var items = await coupons
            .OrderByDescending(c => c.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(c => Project(c, clock.UtcNow))
            .ToListAsync(ct);

        return new PagedResult<CouponDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<Result<CouponDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var coupon = await db.Coupons
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => Project(c, now))
            .FirstOrDefaultAsync(ct);

        return coupon is null ? Result<CouponDto>.NotFound("Coupon not found.") : Result<CouponDto>.Success(coupon);
    }

    public async Task<Result<CouponDto>> CreateAsync(SaveCouponRequest request, CancellationToken ct = default)
    {
        var validation = Validate(request);

        if (!validation.Succeeded)
        {
            return Result<CouponDto>.Failure(validation.Error!);
        }

        // Stored upper-case so lookups are case-insensitive without a collation dependency.
        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Coupons.AnyAsync(c => c.Code == code, ct))
        {
            return Result<CouponDto>.Conflict("A coupon with this code already exists.");
        }

        var coupon = new Coupon { Code = code };
        Apply(coupon, request);

        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);

        return await GetAsync(coupon.Id, ct);
    }

    public async Task<Result<CouponDto>> UpdateAsync(
        Guid id, SaveCouponRequest request, CancellationToken ct = default)
    {
        var validation = Validate(request);

        if (!validation.Succeeded)
        {
            return Result<CouponDto>.Failure(validation.Error!);
        }

        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (coupon is null)
        {
            return Result<CouponDto>.NotFound("Coupon not found.");
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (code != coupon.Code && await db.Coupons.AnyAsync(c => c.Code == code && c.Id != id, ct))
        {
            return Result<CouponDto>.Conflict("A coupon with this code already exists.");
        }

        coupon.Code = code;
        Apply(coupon, request);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (coupon is null)
        {
            return Result.NotFound("Coupon not found.");
        }

        // Redeemed coupons are referenced by order history. Deleting would orphan that record, so
        // deactivation is the correct way to retire one.
        if (await db.CouponRedemptions.AnyAsync(r => r.CouponId == id, ct))
        {
            return Result.Conflict(
                "This coupon has been redeemed on past orders. Deactivate it instead of deleting it.");
        }

        db.Coupons.Remove(coupon);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static Result Validate(SaveCouponRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure("Give the coupon a code.");
        }

        if (request.DiscountType == DiscountType.Percentage && request.Value is <= 0 or > 100)
        {
            return Result.Failure("A percentage discount must be between 1 and 100.");
        }

        if (request.DiscountType == DiscountType.FixedAmount && request.Value <= 0)
        {
            return Result.Failure("A fixed discount must be greater than zero.");
        }

        if (request.StartsAt is { } starts && request.EndsAt is { } ends && ends <= starts)
        {
            return Result.Failure("The end date must be after the start date.");
        }

        return Result.Success();
    }

    private static void Apply(Coupon coupon, SaveCouponRequest request)
    {
        coupon.Description = request.Description;
        coupon.DiscountType = request.DiscountType;
        coupon.Value = request.Value;
        coupon.MinOrderAmount = request.MinOrderAmount;
        coupon.MaxDiscountAmount = request.MaxDiscountAmount;
        coupon.Scope = request.Scope;
        coupon.UsageLimit = request.UsageLimit;
        coupon.UsageLimitPerCustomer = request.UsageLimitPerCustomer;
        coupon.FirstOrderOnly = request.FirstOrderOnly;
        coupon.StartsAt = request.StartsAt;
        coupon.EndsAt = request.EndsAt;
        coupon.IsActive = request.IsActive;
    }

    /// <summary>
    /// Validity is evaluated in the projection rather than read from the entity's computed
    /// property, because the entity's version cannot be translated to SQL.
    /// </summary>
    private static CouponDto Project(Coupon c, DateTimeOffset now) => new(
        c.Id, c.Code, c.Description, c.DiscountType, c.Value, c.MinOrderAmount, c.MaxDiscountAmount,
        c.Scope, c.UsageLimit, c.UsageLimitPerCustomer, c.UsedCount, c.FirstOrderOnly,
        c.StartsAt, c.EndsAt, c.IsActive,
        c.IsActive
            && (c.StartsAt == null || c.StartsAt <= now)
            && (c.EndsAt == null || c.EndsAt >= now)
            && (c.UsageLimit == null || c.UsedCount < c.UsageLimit));
}

// ==============================================================================================
// Customers
// ==============================================================================================

public sealed record CustomerListItemDto(
    Guid Id,
    Guid UserId,
    string FullName,
    string Email,
    string? Phone,
    int TotalOrders,
    decimal TotalSpent,
    decimal AverageOrderValue,
    DateTimeOffset? LastOrderAt,
    bool IsActive,
    bool AcceptsMarketing,
    DateTimeOffset CreatedAt);

public sealed record CustomerDetailDto(
    CustomerListItemDto Summary,
    IReadOnlyList<Commerce.AddressSummaryDto> Addresses,
    IReadOnlyList<Application.Commerce.OrderSummaryDto> RecentOrders,
    string? Notes);

public sealed record CustomerQuery : PagedQuery
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; } = true;
}

public interface ICustomerAdminService
{
    Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerQuery query, CancellationToken ct = default);
    Task<Result<CustomerDetailDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result> UpdateNotesAsync(Guid id, string? notes, CancellationToken ct = default);
}

public sealed class CustomerAdminService(StoreDbContext db) : ICustomerAdminService
{
    public async Task<PagedResult<CustomerListItemDto>> ListAsync(
        CustomerQuery query, CancellationToken ct = default)
    {
        var customers = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            customers = customers.Where(c =>
                c.User.Email!.Contains(term) ||
                c.User.FirstName.Contains(term) ||
                c.User.LastName.Contains(term) ||
                c.User.PhoneNumber!.Contains(term));
        }

        if (query.IsActive is { } active)
        {
            customers = customers.Where(c => c.User.IsActive == active);
        }

        customers = query.SortBy?.ToLowerInvariant() switch
        {
            "spent" => query.Descending ? customers.OrderByDescending(c => c.TotalSpent) : customers.OrderBy(c => c.TotalSpent),
            "orders" => query.Descending ? customers.OrderByDescending(c => c.TotalOrders) : customers.OrderBy(c => c.TotalOrders),
            "lastorder" => query.Descending ? customers.OrderByDescending(c => c.LastOrderAt) : customers.OrderBy(c => c.LastOrderAt),
            _ => query.Descending ? customers.OrderByDescending(c => c.CreatedAt) : customers.OrderBy(c => c.CreatedAt)
        };

        var total = await customers.CountAsync(ct);

        var items = await customers
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(Projection)
            .ToListAsync(ct);

        return new PagedResult<CustomerListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<Result<CustomerDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var summary = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        if (summary is null)
        {
            return Result<CustomerDetailDto>.NotFound("Customer not found.");
        }

        var addresses = await db.Addresses
            .AsNoTracking()
            .Where(a => a.CustomerId == id)
            .Select(a => new Commerce.AddressSummaryDto(
                a.Id, a.Label, a.FullName, a.Phone, a.Line1, a.Line2, a.District,
                a.City, a.Region, a.PostalCode, a.CountryCode, a.IsDefaultShipping))
            .ToListAsync(ct);

        var recentOrders = await db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.PlacedAt)
            .Take(10)
            .Select(Commerce.OrderProjections.Summary)
            .ToListAsync(ct);

        var notes = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.Notes)
            .FirstOrDefaultAsync(ct);

        return Result<CustomerDetailDto>.Success(new CustomerDetailDto(summary, addresses, recentOrders, notes));
    }

    public async Task<Result> UpdateNotesAsync(Guid id, string? notes, CancellationToken ct = default)
    {
        var updated = await db.Customers
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Notes, notes), ct);

        return updated == 0 ? Result.NotFound("Customer not found.") : Result.Success();
    }

    /// <summary>
    /// Average order value is computed in SQL rather than read from the entity's calculated
    /// property, which EF cannot translate.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Domain.Customers.Customer, CustomerListItemDto>> Projection =
        c => new CustomerListItemDto(
            c.Id,
            c.UserId,
            c.User.FirstName + " " + c.User.LastName,
            c.User.Email!,
            c.User.PhoneNumber,
            c.TotalOrders,
            c.TotalSpent,
            c.TotalOrders > 0 ? c.TotalSpent / c.TotalOrders : 0m,
            c.LastOrderAt,
            c.User.IsActive,
            c.AcceptsMarketing,
            c.CreatedAt);
}

// ==============================================================================================
// Settings
// ==============================================================================================

public sealed record SettingDto(
    string Key, string? Value, string Group, string DataType,
    string? DisplayName, string? Description, bool IsPublic);

public interface ISettingsService
{
    /// <summary>Public settings only — safe to serve to an anonymous storefront.</summary>
    Task<IReadOnlyDictionary<string, string?>> GetPublicAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result> UpdateAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
}

public sealed class SettingsService(
    StoreDbContext db,
    ICacheService cache,
    IDateTimeProvider clock,
    ICurrentUser currentUser) : ISettingsService
{
    public async Task<IReadOnlyDictionary<string, string?>> GetPublicAsync(CancellationToken ct = default)
    {
        var settings = await cache.GetOrCreateAsync(
            CacheKeys.PublicSettings,
            async token => await db.Settings
                .AsNoTracking()
                // Filtered in the query, not after. Anything not explicitly public — SMTP
                // credentials included — must never reach an anonymous caller.
                .Where(s => s.IsPublic)
                .Select(s => new { s.Key, s.Value })
                .ToDictionaryAsync(s => s.Key, s => s.Value, token),
            TimeSpan.FromMinutes(30),
            [CacheKeys.Tags.Settings],
            ct);

        return settings;
    }

    public async Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Settings
            .AsNoTracking()
            .OrderBy(s => s.Group).ThenBy(s => s.Key)
            .Select(s => new SettingDto(
                s.Key, s.Value, s.Group, s.DataType, s.DisplayName, s.Description, s.IsPublic))
            .ToListAsync(ct);

    public async Task<Result> UpdateAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        if (values.Count == 0)
        {
            return Result.Success();
        }

        var keys = values.Keys.ToList();
        var settings = await db.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);

        var unknown = keys.Except(settings.Select(s => s.Key)).ToList();

        if (unknown.Count > 0)
        {
            // Settings are seeded from code, so an unknown key is a client bug rather than a new
            // setting to create silently.
            return Result.Failure($"Unknown settings: {string.Join(", ", unknown)}");
        }

        foreach (var setting in settings)
        {
            setting.Value = values[setting.Key];
            setting.UpdatedAt = clock.UtcNow;
            setting.UpdatedBy = currentUser.UserId;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Settings, ct);

        return Result.Success();
    }
}

// ==============================================================================================
// Content — banners, pages, blog
// ==============================================================================================

public sealed record BannerDto(
    Guid Id, string Title, string? Subtitle, string ImageUrl, string? MobileImageUrl,
    int Width, int Height, string? BlurHash, string? LinkUrl, string? ButtonText,
    BannerPosition Position, int DisplayOrder, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    bool IsActive, string? AltText);

public sealed record ContentPageDto(
    Guid Id, string Title, string Slug, string Body, ContentStatus Status,
    string? MetaTitle, string? MetaDescription, bool ShowInFooter, int DisplayOrder, bool IsSystemPage);

public interface IContentService
{
    Task<IReadOnlyList<BannerDto>> GetActiveBannersAsync(BannerPosition position, CancellationToken ct = default);
    Task<IReadOnlyList<BannerDto>> GetAllBannersAsync(CancellationToken ct = default);
    Task<Result<BannerDto>> SaveBannerAsync(Guid? id, BannerDto request, CancellationToken ct = default);
    Task<Result> DeleteBannerAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ContentPageDto>> GetFooterPagesAsync(CancellationToken ct = default);
    Task<Result<ContentPageDto>> GetPageBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<ContentPageDto>> GetAllPagesAsync(CancellationToken ct = default);
    Task<Result<ContentPageDto>> SavePageAsync(Guid? id, ContentPageDto request, CancellationToken ct = default);
    Task<Result> DeletePageAsync(Guid id, CancellationToken ct = default);
}

public sealed class ContentService(
    StoreDbContext db,
    ICacheService cache,
    IDateTimeProvider clock) : IContentService
{
    public async Task<IReadOnlyList<BannerDto>> GetActiveBannersAsync(
        BannerPosition position, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // The date window is evaluated in SQL so an expired banner is never transferred and then
        // filtered away in memory.
        return await db.Banners
            .AsNoTracking()
            .Where(b => b.Position == position
                        && b.IsActive
                        && (b.StartsAt == null || b.StartsAt <= now)
                        && (b.EndsAt == null || b.EndsAt >= now))
            .OrderBy(b => b.DisplayOrder)
            .Select(Project)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BannerDto>> GetAllBannersAsync(CancellationToken ct = default) =>
        await db.Banners
            .AsNoTracking()
            .OrderBy(b => b.Position).ThenBy(b => b.DisplayOrder)
            .Select(Project)
            .ToListAsync(ct);

    public async Task<Result<BannerDto>> SaveBannerAsync(
        Guid? id, BannerDto request, CancellationToken ct = default)
    {
        Banner banner;

        if (id is { } bannerId)
        {
            var existing = await db.Banners.FirstOrDefaultAsync(b => b.Id == bannerId, ct);

            if (existing is null)
            {
                return Result<BannerDto>.NotFound("Banner not found.");
            }

            banner = existing;
        }
        else
        {
            banner = new Banner();
            db.Banners.Add(banner);
        }

        banner.Title = request.Title.Trim();
        banner.Subtitle = request.Subtitle;
        banner.ImageUrl = request.ImageUrl;
        banner.MobileImageUrl = request.MobileImageUrl;
        banner.Width = request.Width;
        banner.Height = request.Height;
        banner.BlurHash = request.BlurHash;
        banner.LinkUrl = request.LinkUrl;
        banner.ButtonText = request.ButtonText;
        banner.Position = request.Position;
        banner.DisplayOrder = request.DisplayOrder;
        banner.StartsAt = request.StartsAt;
        banner.EndsAt = request.EndsAt;
        banner.IsActive = request.IsActive;
        banner.AltText = request.AltText;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);

        return Result<BannerDto>.Success(request with { Id = banner.Id });
    }

    public async Task<Result> DeleteBannerAsync(Guid id, CancellationToken ct = default)
    {
        var deleted = await db.Banners.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Catalog, ct);

        return deleted == 0 ? Result.NotFound("Banner not found.") : Result.Success();
    }

    public async Task<IReadOnlyList<ContentPageDto>> GetFooterPagesAsync(CancellationToken ct = default) =>
        await db.ContentPages
            .AsNoTracking()
            .Where(p => p.ShowInFooter && p.Status == ContentStatus.Published)
            .OrderBy(p => p.DisplayOrder)
            .Select(ProjectPage)
            .ToListAsync(ct);

    public async Task<Result<ContentPageDto>> GetPageBySlugAsync(string slug, CancellationToken ct = default)
    {
        var page = await db.ContentPages
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.Status == ContentStatus.Published)
            .Select(ProjectPage)
            .FirstOrDefaultAsync(ct);

        return page is null ? Result<ContentPageDto>.NotFound("Page not found.") : Result<ContentPageDto>.Success(page);
    }

    public async Task<IReadOnlyList<ContentPageDto>> GetAllPagesAsync(CancellationToken ct = default) =>
        await db.ContentPages
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .Select(ProjectPage)
            .ToListAsync(ct);

    public async Task<Result<ContentPageDto>> SavePageAsync(
        Guid? id, ContentPageDto request, CancellationToken ct = default)
    {
        ContentPage page;

        if (id is { } pageId)
        {
            var existing = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == pageId, ct);

            if (existing is null)
            {
                return Result<ContentPageDto>.NotFound("Page not found.");
            }

            page = existing;
        }
        else
        {
            page = new ContentPage();
            db.ContentPages.Add(page);
        }

        // System pages are linked from the footer and checkout by slug, so their slug is frozen.
        if (!page.IsSystemPage)
        {
            page.Slug = await SlugGenerator.GenerateUniqueAsync(
                request.Slug, request.Title,
                async (s, token) => await db.ContentPages.AnyAsync(p => p.Slug == s && p.Id != page.Id, token),
                ct);
        }

        page.Title = request.Title.Trim();
        page.Body = request.Body;
        page.Status = request.Status;
        page.MetaTitle = request.MetaTitle;
        page.MetaDescription = request.MetaDescription;
        page.ShowInFooter = request.ShowInFooter;
        page.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync(ct);

        return Result<ContentPageDto>.Success(request with { Id = page.Id, Slug = page.Slug });
    }

    public async Task<Result> DeletePageAsync(Guid id, CancellationToken ct = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (page is null)
        {
            return Result.NotFound("Page not found.");
        }

        if (page.IsSystemPage)
        {
            return Result.Forbidden(
                "This page is linked from the footer and checkout. Unpublish it instead of deleting it.");
        }

        db.ContentPages.Remove(page);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static readonly System.Linq.Expressions.Expression<Func<Banner, BannerDto>> Project =
        b => new BannerDto(
            b.Id, b.Title, b.Subtitle, b.ImageUrl, b.MobileImageUrl, b.Width, b.Height, b.BlurHash,
            b.LinkUrl, b.ButtonText, b.Position, b.DisplayOrder, b.StartsAt, b.EndsAt, b.IsActive, b.AltText);

    private static readonly System.Linq.Expressions.Expression<Func<ContentPage, ContentPageDto>> ProjectPage =
        p => new ContentPageDto(
            p.Id, p.Title, p.Slug, p.Body, p.Status, p.MetaTitle, p.MetaDescription,
            p.ShowInFooter, p.DisplayOrder, p.IsSystemPage);
}

// ==============================================================================================
// Audit log
// ==============================================================================================

public sealed record AuditLogDto(
    Guid Id, Guid? UserId, string? UserName, string Action, string EntityType,
    string? EntityId, string? EntityName, string? Changes, string? IpAddress,
    string? CorrelationId, DateTimeOffset CreatedAt);

public sealed record AuditQuery : PagedQuery
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public Guid? UserId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public interface IAuditService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default);
}

public sealed class AuditService(StoreDbContext db) : IAuditService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default)
    {
        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Action)) logs = logs.Where(l => l.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) logs = logs.Where(l => l.EntityType == query.EntityType);
        if (query.UserId is { } userId) logs = logs.Where(l => l.UserId == userId);
        if (query.From is { } from) logs = logs.Where(l => l.CreatedAt >= from);
        if (query.To is { } to) logs = logs.Where(l => l.CreatedAt <= to);

        var total = await logs.CountAsync(ct);

        var items = await logs
            .OrderByDescending(l => l.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(l => new AuditLogDto(
                l.Id, l.UserId, l.UserName, l.Action, l.EntityType, l.EntityId,
                l.EntityName, l.Changes, l.IpAddress, l.CorrelationId, l.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }
}

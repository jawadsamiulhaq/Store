using Microsoft.EntityFrameworkCore;
using Store.Application.Catalog;
using Store.Application.Common;
using Store.Domain.Catalog;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Catalog;

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Result<BrandDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<BrandDto>> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Result<BrandDto>> CreateAsync(CreateBrandRequest request, CancellationToken ct = default);
    Task<Result<BrandDto>> UpdateAsync(Guid id, UpdateBrandRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class BrandService(StoreDbContext db, ICacheService cache) : IBrandService
{
    public async Task<IReadOnlyList<BrandDto>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        // Only the public list is cached. The admin view includes inactive brands and is read far
        // less often, so caching it would add invalidation complexity for no real benefit.
        if (includeInactive)
        {
            return await QueryBrands(includeInactive: true).ToListAsync(ct);
        }

        return await cache.GetOrCreateAsync(
            CacheKeys.BrandList,
            async token => await QueryBrands(includeInactive: false).ToListAsync(token),
            TimeSpan.FromMinutes(30),
            [CacheKeys.Tags.Brands, CacheKeys.Tags.Catalog],
            ct);
    }

    private IQueryable<BrandDto> QueryBrands(bool includeInactive)
    {
        var brands = db.Brands.AsNoTracking();

        if (!includeInactive)
        {
            brands = brands.Where(b => b.IsActive);
        }

        return brands
            .OrderByDescending(b => b.IsFeatured)
            .ThenBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .Select(b => new BrandDto(
                b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.CountryOfOrigin,
                b.DisplayOrder, b.IsActive, b.IsFeatured, b.ProductCount));
    }

    public async Task<Result<BrandDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var brand = await QueryBrands(includeInactive: true).FirstOrDefaultAsync(b => b.Id == id, ct);

        return brand is null ? Result<BrandDto>.NotFound("Brand not found.") : Result<BrandDto>.Success(brand);
    }

    public async Task<Result<BrandDto>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var brand = await QueryBrands(includeInactive: true).FirstOrDefaultAsync(b => b.Slug == slug, ct);

        return brand is null ? Result<BrandDto>.NotFound("Brand not found.") : Result<BrandDto>.Success(brand);
    }

    public async Task<Result<BrandDto>> CreateAsync(CreateBrandRequest request, CancellationToken ct = default)
    {
        var slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Brands.AnyAsync(b => b.Slug == s, token),
            ct);

        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            LogoUrl = request.LogoUrl,
            CountryOfOrigin = request.CountryOfOrigin,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured
        };

        db.Brands.Add(brand);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Brands, ct);

        return await GetAsync(brand.Id, ct);
    }

    public async Task<Result<BrandDto>> UpdateAsync(
        Guid id, UpdateBrandRequest request, CancellationToken ct = default)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);

        if (brand is null)
        {
            return Result<BrandDto>.NotFound("Brand not found.");
        }

        brand.Slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Brands.AnyAsync(b => b.Slug == s && b.Id != id, token),
            ct);

        brand.Name = request.Name.Trim();
        brand.Description = request.Description;
        brand.LogoUrl = request.LogoUrl;
        brand.CountryOfOrigin = request.CountryOfOrigin;
        brand.DisplayOrder = request.DisplayOrder;
        brand.IsActive = request.IsActive;
        brand.IsFeatured = request.IsFeatured;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Brands, ct);

        return await GetAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);

        if (brand is null)
        {
            return Result.NotFound("Brand not found.");
        }

        var productCount = await db.Products.CountAsync(p => p.BrandId == id, ct);

        if (productCount > 0)
        {
            return Result.Conflict(
                $"This brand is used by {productCount} product(s). Reassign them before deleting it.");
        }

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Brands, ct);

        return Result.Success();
    }
}

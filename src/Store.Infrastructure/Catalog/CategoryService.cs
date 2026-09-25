using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Catalog;
using Store.Application.Common;
using Store.Domain.Catalog;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Catalog;

public interface ICategoryService
{
    /// <summary>The navigation tree. Cached — read on every page, written rarely.</summary>
    Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Result<CategoryDto>> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Result<CategoryDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<Result<CategoryDto>> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Recomputes the maintained product counts for every category.</summary>
    Task RefreshProductCountsAsync(CancellationToken ct = default);
}

public sealed class CategoryService(
    StoreDbContext db,
    ICacheService cache,
    ILogger<CategoryService> logger) : ICategoryService
{
    private static readonly TimeSpan TreeCacheLifetime = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var tree = await cache.GetOrCreateAsync(
            CacheKeys.CategoryTree,
            async token => await BuildTreeAsync(token),
            TreeCacheLifetime,
            [CacheKeys.Tags.Categories, CacheKeys.Tags.Catalog],
            ct);

        return tree;
    }

    /// <summary>
    /// Loads the whole (small) category table in one query and assembles the tree in memory.
    /// </summary>
    /// <remarks>
    /// A grocery has tens of categories, not thousands, so one flat read plus an in-memory
    /// assembly beats a recursive CTE or — far worse — a query per level. The result is cached,
    /// so this runs on a cache miss, not per request.
    /// </remarks>
    private async Task<List<CategoryTreeDto>> BuildTreeAsync(CancellationToken ct)
    {
        var flat = await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ShowInMenu)
            .OrderBy(c => c.Depth).ThenBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.ImageUrl, c.IconName, c.ParentId, c.ProductCount
            })
            .ToListAsync(ct);

        var childrenByParent = flat
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<CategoryTreeDto> Build(Guid? parentId) =>
            (parentId is null
                ? flat.Where(c => c.ParentId is null)
                : childrenByParent.TryGetValue(parentId.Value, out var kids) ? kids : [])
            .Select(c => new CategoryTreeDto(
                c.Id, c.Name, c.Slug, c.ImageUrl, c.IconName, c.ProductCount, Build(c.Id)))
            .ToList();

        return Build(null);
    }

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Depth).ThenBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.IconName,
                c.ParentId, c.Depth, c.DisplayOrder, c.IsActive, c.ShowInMenu, c.ProductCount))
            .ToListAsync(ct);
    }

    public async Task<Result<CategoryDto>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var category = await db.Categories
            .AsNoTracking()
            .Where(c => c.Slug == slug)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.IconName,
                c.ParentId, c.Depth, c.DisplayOrder, c.IsActive, c.ShowInMenu, c.ProductCount))
            .FirstOrDefaultAsync(ct);

        return category is null
            ? Result<CategoryDto>.NotFound("Category not found.")
            : Result<CategoryDto>.Success(category);
    }

    public async Task<Result<CategoryDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.IconName,
                c.ParentId, c.Depth, c.DisplayOrder, c.IsActive, c.ShowInMenu, c.ProductCount))
            .FirstOrDefaultAsync(ct);

        return category is null
            ? Result<CategoryDto>.NotFound("Category not found.")
            : Result<CategoryDto>.Success(category);
    }

    public async Task<Result<CategoryDto>> CreateAsync(
        CreateCategoryRequest request, CancellationToken ct = default)
    {
        var parent = await ResolveParentAsync(request.ParentId, ct);

        if (request.ParentId is not null && parent is null)
        {
            return Result<CategoryDto>.Failure("The selected parent category does not exist.");
        }

        var slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Categories.AnyAsync(c => c.Slug == s, token),
            ct);

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            IconName = request.IconName,
            ParentId = request.ParentId,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            ShowInMenu = request.ShowInMenu,
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            Depth = parent is null ? 0 : parent.Depth + 1
        };

        // Path is assembled from the parent's, which is why the parent must be resolved first.
        category.Path = BuildPath(parent?.Path, slug);

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);

        logger.LogInformation("Created category {Name} ({Slug}) at depth {Depth}", category.Name, slug, category.Depth);

        return await GetAsync(category.Id, ct);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(
        Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Result<CategoryDto>.NotFound("Category not found.");
        }

        if (request.ParentId == id)
        {
            return Result<CategoryDto>.Failure("A category cannot be its own parent.");
        }

        // Reparenting under a descendant would detach the subtree from the root and create a
        // cycle. The materialised path makes this check a single string comparison.
        if (request.ParentId is { } newParentId && newParentId != category.ParentId)
        {
            var newParent = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == newParentId, ct);

            if (newParent is null)
            {
                return Result<CategoryDto>.Failure("The selected parent category does not exist.");
            }

            if (newParent.Path.StartsWith(category.Path, StringComparison.Ordinal))
            {
                return Result<CategoryDto>.Failure(
                    "That category is inside this one. Moving it there would create a loop.");
            }
        }

        var oldPath = category.Path;

        var slug = await SlugGenerator.GenerateUniqueAsync(
            request.Slug, request.Name,
            async (s, token) => await db.Categories.AnyAsync(c => c.Slug == s && c.Id != id, token),
            ct);

        category.Name = request.Name.Trim();
        category.Slug = slug;
        category.Description = request.Description;
        category.ImageUrl = request.ImageUrl;
        category.IconName = request.IconName;
        category.ParentId = request.ParentId;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.ShowInMenu = request.ShowInMenu;
        category.MetaTitle = request.MetaTitle;
        category.MetaDescription = request.MetaDescription;

        var parent = await ResolveParentAsync(request.ParentId, ct);
        category.Depth = parent is null ? 0 : parent.Depth + 1;
        category.Path = BuildPath(parent?.Path, slug);

        await db.SaveChangesAsync(ct);

        // If this node moved or was renamed, every descendant's stored path is now wrong.
        if (!string.Equals(oldPath, category.Path, StringComparison.Ordinal))
        {
            await RepairDescendantPathsAsync(oldPath, category.Path, ct);
        }

        await InvalidateAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return Result.NotFound("Category not found.");
        }

        // Blocked rather than cascaded. Deleting a parent that silently takes its whole subtree
        // with it is the kind of destructive surprise an admin UI should never spring on someone.
        var childCount = await db.Categories.CountAsync(c => c.ParentId == id, ct);

        if (childCount > 0)
        {
            return Result.Conflict(
                $"This category has {childCount} subcategor{(childCount == 1 ? "y" : "ies")}. Move or delete them first.");
        }

        var productCount = await db.Products.CountAsync(p => p.CategoryId == id, ct);

        if (productCount > 0)
        {
            return Result.Conflict(
                $"This category still contains {productCount} product(s). Reassign them first.");
        }

        // Soft delete — the auditing interceptor converts Remove() on an ISoftDeletable.
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// Recomputes every category's product count, including descendants.
    /// </summary>
    /// <remarks>
    /// Runs after bulk imports and on a schedule, not per write — the counts are a display
    /// convenience, so brief staleness is acceptable and constant recomputation is not.
    /// Counting per category via the materialised path means one pass, not a query per node.
    /// </remarks>
    public async Task RefreshProductCountsAsync(CancellationToken ct = default)
    {
        var categories = await db.Categories.ToListAsync(ct);

        var directCounts = await db.Products
            .AsNoTracking()
            .Where(p => p.Status == Domain.Enums.ProductStatus.Active && p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        foreach (var category in categories)
        {
            // A parent's count includes everything beneath it, which is what a shopper expects
            // when the menu says "Frozen (246)".
            category.ProductCount = categories
                .Where(c => c.Path.StartsWith(category.Path, StringComparison.Ordinal))
                .Sum(c => directCounts.GetValueOrDefault(c.Id));
        }

        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);

        logger.LogInformation("Refreshed product counts for {Count} categories", categories.Count);
    }

    private async Task<Category?> ResolveParentAsync(Guid? parentId, CancellationToken ct) =>
        parentId is null
            ? null
            : await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);

    /// <summary>Always slash-delimited and slash-terminated, so prefix matching cannot half-match a sibling.</summary>
    private static string BuildPath(string? parentPath, string slug) =>
        string.IsNullOrEmpty(parentPath) ? $"/{slug}/" : $"{parentPath}{slug}/";

    /// <summary>
    /// Rewrites the stored path of every descendant after a node is renamed or moved, in one
    /// set-based statement rather than loading and saving the subtree.
    /// </summary>
    private async Task RepairDescendantPathsAsync(string oldPath, string newPath, CancellationToken ct)
    {
        var affected = await db.Categories
            .Where(c => c.Path.StartsWith(oldPath) && c.Path != newPath)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Path, c => newPath + c.Path.Substring(oldPath.Length)), ct);

        if (affected > 0)
        {
            logger.LogInformation("Rewrote paths for {Count} descendant categories", affected);
        }
    }

    private Task InvalidateAsync(CancellationToken ct) =>
        cache.RemoveByTagAsync(CacheKeys.Tags.Categories, ct);
}

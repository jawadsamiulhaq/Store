using Store.Domain.Common;

namespace Store.Domain.Catalog;

/// <summary>
/// Self-referencing category tree.
/// </summary>
/// <remarks>
/// <see cref="Path"/> is a materialised ancestor path (<c>/frozen/meat/chicken/</c>). Browsing a
/// parent category must include everything beneath it, and doing that with a recursive CTE on
/// every catalogue request is needless work. A <c>LIKE '/frozen/%'</c> against an indexed path
/// column resolves the whole subtree in one seek.
/// </remarks>
public class Category : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Clean, stable URL segment — <c>ready-canned-food</c>.
    /// The legacy store emitted <c>ready---canned-food-639</c>: doubled separators from naive
    /// punctuation replacement plus a random numeric suffix to dodge collisions. Uniqueness is
    /// enforced by the database here, and collisions are resolved with a readable <c>-2</c> suffix.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? IconName { get; set; }

    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = [];

    /// <summary>Materialised ancestor path, always slash-delimited and slash-terminated.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Root categories are depth 0.</summary>
    public int Depth { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Surfaces this category in the main navigation bar.</summary>
    public bool ShowInMenu { get; set; } = true;

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    /// <summary>
    /// Maintained count of active products in this category (including descendants).
    /// Rendered on every navigation menu; recomputing it per request would mean a COUNT per node.
    /// </summary>
    public int ProductCount { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}

public class Brand : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? CountryOfOrigin { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    public int ProductCount { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}

public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ICollection<ProductTag> ProductTags { get; set; } = [];
}

public class ProductTag
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

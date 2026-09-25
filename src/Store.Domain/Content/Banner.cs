using Store.Domain.Common;
using Store.Domain.Enums;

namespace Store.Domain.Content;

/// <summary>
/// A merchandising banner. Carries an explicit mobile image and intrinsic dimensions because the
/// home hero is the LCP element on most visits — the browser must be able to reserve its exact
/// box and pick the right source before any bytes arrive.
/// </summary>
public class Banner : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Portrait crop for narrow viewports. Avoids shipping a 1600px-wide hero to a phone.</summary>
    public string? MobileImageUrl { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Base64 LQIP shown while the hero decodes, so the slot is never blank.</summary>
    public string? BlurHash { get; set; }

    public string? LinkUrl { get; set; }
    public string? ButtonText { get; set; }

    public BannerPosition Position { get; set; } = BannerPosition.HomeHero;
    public int DisplayOrder { get; set; }

    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;

    public string? AltText { get; set; }
}

/// <summary>A blog article. Kept from the legacy store, which exposed <c>/api/blogs</c>.</summary>
public class BlogPost : AuditableEntity, ISoftDeletable
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string Body { get; set; } = string.Empty;

    public string? CoverImageUrl { get; set; }
    public int CoverWidth { get; set; }
    public int CoverHeight { get; set; }

    public string? AuthorName { get; set; }
    public Guid? AuthorId { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    public int ViewCount { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

/// <summary>
/// A static CMS page — About, Contact, Delivery, Returns, Privacy, Terms.
/// Editable by staff so legal copy does not require a deploy.
/// </summary>
public class ContentPage : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    /// <summary>Renders a link in the footer.</summary>
    public bool ShowInFooter { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>System pages cannot be deleted — the footer and checkout link to them by slug.</summary>
    public bool IsSystemPage { get; set; }
}

/// <summary>
/// An uploaded file in the media library. Dimensions and derivative URLs are recorded at upload
/// time so that every consumer can emit a correct <c>srcset</c> and reserve layout space.
/// </summary>
public class MediaAsset : AuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }

    /// <summary>Modern-format derivative, served first via <c>&lt;picture&gt;</c>.</summary>
    public string? WebpUrl { get; set; }
    public string? AvifUrl { get; set; }

    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public string? BlurHash { get; set; }

    public string? AltText { get; set; }
    public string? Folder { get; set; }

    /// <summary>
    /// Reference count across products, banners and content. The legacy store exposed a
    /// <c>check-usage</c> endpoint for this; tracking it directly makes safe deletion cheap.
    /// </summary>
    public int UsageCount { get; set; }
}

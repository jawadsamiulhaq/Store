using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Content;
using Store.Domain.Platform;

namespace Store.Infrastructure.Persistence.Configurations;

public class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> b)
    {
        b.ToTable("Banners");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Subtitle).HasMaxLength(500);
        b.Property(x => x.ImageUrl).HasMaxLength(1000).IsRequired();
        b.Property(x => x.MobileImageUrl).HasMaxLength(1000);
        b.Property(x => x.BlurHash).HasMaxLength(200);
        b.Property(x => x.LinkUrl).HasMaxLength(1000);
        b.Property(x => x.ButtonText).HasMaxLength(50);
        b.Property(x => x.AltText).HasMaxLength(300);

        // Home hero lookup: active banners for a position, in order, within their date window.
        b.HasIndex(x => new { x.Position, x.IsActive, x.DisplayOrder })
            .HasDatabaseName("IX_Banners_Position_Active_Order");
    }
}

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> b)
    {
        b.ToTable("BlogPosts");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(350).IsRequired();
        b.Property(x => x.Excerpt).HasMaxLength(500);
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.CoverImageUrl).HasMaxLength(1000);
        b.Property(x => x.AuthorName).HasMaxLength(200);
        b.Property(x => x.MetaTitle).HasMaxLength(200);
        b.Property(x => x.MetaDescription).HasMaxLength(500);

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("IX_BlogPosts_Slug");
        b.HasIndex(x => new { x.Status, x.PublishedAt }).HasDatabaseName("IX_BlogPosts_Status_Published");

        b.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    public void Configure(EntityTypeBuilder<ContentPage> b)
    {
        b.ToTable("ContentPages");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.MetaTitle).HasMaxLength(200);
        b.Property(x => x.MetaDescription).HasMaxLength(500);

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("IX_ContentPages_Slug");
        b.HasIndex(x => new { x.ShowInFooter, x.DisplayOrder });
    }
}

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> b)
    {
        b.ToTable("MediaAssets");
        b.HasKey(x => x.Id);

        b.Property(x => x.FileName).HasMaxLength(300).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(300).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
        b.Property(x => x.WebpUrl).HasMaxLength(1000);
        b.Property(x => x.AvifUrl).HasMaxLength(1000);
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.BlurHash).HasMaxLength(200);
        b.Property(x => x.AltText).HasMaxLength(300);
        b.Property(x => x.Folder).HasMaxLength(200);

        b.HasIndex(x => new { x.Folder, x.CreatedAt });
        b.HasIndex(x => x.FileName);
    }
}

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.ToTable("Settings");
        b.HasKey(x => x.Id);

        b.Property(x => x.Key).HasMaxLength(150).IsRequired();
        b.Property(x => x.Value).HasMaxLength(4000);
        b.Property(x => x.Group).HasMaxLength(50).IsRequired();
        b.Property(x => x.DataType).HasMaxLength(20).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(500);

        b.HasIndex(x => x.Key).IsUnique().HasDatabaseName("IX_Settings_Key");

        // The storefront bootstrap fetches every public setting in one query.
        b.HasIndex(x => new { x.Group, x.IsPublic });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.Action).HasMaxLength(50).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.EntityName).HasMaxLength(400);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(500);
        b.Property(x => x.CorrelationId).HasMaxLength(64);

        // Audit search is always "recent first", optionally narrowed by actor or by entity.
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_AuditLogs_CreatedAt");
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.LinkUrl).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.Property(x => x.RequiredPermission).HasMaxLength(100);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The bell icon polls for unread count. This index answers it without a table scan.
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt })
            .HasDatabaseName("IX_Notifications_User_Unread");

        // Staff broadcasts targeted by permission (low stock, new order).
        b.HasIndex(x => new { x.RequiredPermission, x.CreatedAt });
    }
}

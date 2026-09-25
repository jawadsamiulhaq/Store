using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Catalog;
using Store.Domain.Enums;

namespace Store.Infrastructure.Persistence.Configurations;

// ------------------------------------------------------------------------------------------
// A note on query filters for the catalogue's child entities.
//
// Product is soft-deleted and carries a global query filter. Its children — variants, images,
// options, tags — deliberately do NOT repeat that filter.
//
// Repeating it is the intuitive choice and it was measurably wrong. A child filter of
// `x => x.Product.DeletedAt == null` is re-applied inside *every* subquery that touches the
// child, so the product-card projection generated an INNER JOIN back to Products for each of
// its image lookups — roughly ten redundant joins per row, on the single most-requested query
// in the application.
//
// The filter is unnecessary because these children are only ever reached by navigating from a
// Product that has already been filtered. The one place to stay alert is querying a child set
// directly (e.g. db.ProductImages), which would see rows belonging to deleted products; do that
// through the parent, or filter explicitly.
// ------------------------------------------------------------------------------------------

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Categories");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.Property(x => x.IconName).HasMaxLength(100);
        b.Property(x => x.Path).HasMaxLength(1000).IsRequired();
        b.Property(x => x.MetaTitle).HasMaxLength(200);
        b.Property(x => x.MetaDescription).HasMaxLength(500);

        // Self-reference. Restrict, not Cascade: deleting a parent must not silently take an
        // entire subtree of categories (and their product links) with it.
        b.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("IX_Categories_Slug");
        b.HasIndex(x => new { x.ParentId, x.DisplayOrder }).HasDatabaseName("IX_Categories_Parent_Order");

        // Serves subtree browse: WHERE Path LIKE '/frozen/%'. A range seek on an indexed prefix,
        // instead of a recursive CTE on every catalogue request.
        b.HasIndex(x => x.Path).HasDatabaseName("IX_Categories_Path");

        b.HasIndex(x => new { x.IsActive, x.ShowInMenu, x.DisplayOrder })
            .HasDatabaseName("IX_Categories_Menu");

        b.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.ToTable("Brands");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.LogoUrl).HasMaxLength(1000);
        b.Property(x => x.CountryOfOrigin).HasMaxLength(100);

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("IX_Brands_Slug");
        b.HasIndex(x => new { x.IsActive, x.DisplayOrder }).HasDatabaseName("IX_Brands_Active_Order");

        b.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(450).IsRequired();
        b.Property(x => x.ShortDescription).HasMaxLength(500);
        b.Property(x => x.Description).HasMaxLength(8000);
        b.Property(x => x.Badge).HasMaxLength(50);
        b.Property(x => x.MetaTitle).HasMaxLength(200);
        b.Property(x => x.MetaDescription).HasMaxLength(500);

        // SetNull, not Cascade: removing a category must not delete the products in it.
        b.HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Brand)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("IX_Products_Slug");

        // ---- Catalogue indexes -----------------------------------------------------------
        // Each of these exists to serve a specific named query. The INCLUDE columns are exactly
        // the fields the product-card projection selects, which makes these covering indexes:
        // the listing is answered from the index alone, with no key lookup back to the table.

        // Category browse + sort. The single most-requested query in the application.
        b.HasIndex(x => new { x.CategoryId, x.Status })
            .IncludeProperties(x => new { x.Name, x.Slug, x.MinPrice, x.MaxPrice, x.InStock, x.RatingAverage, x.CreatedAt })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Category_Status_Covering");

        // Brand browse.
        b.HasIndex(x => new { x.BrandId, x.Status })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Brand_Status");

        // "Sort by price" across the whole catalogue. Filtered so the index holds only the rows
        // the storefront can ever return, keeping it small and hot in the buffer pool.
        b.HasIndex(x => new { x.Status, x.MinPrice })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Status_MinPrice");

        // "Newest first" — the storefront default sort.
        b.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Status_CreatedAt");

        // "Best selling" and "Top rated" sorts, served without touching OrderItems or Reviews.
        b.HasIndex(x => new { x.Status, x.SalesCount })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Status_SalesCount");

        b.HasIndex(x => new { x.Status, x.RatingAverage })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Status_Rating");

        // Home-page merchandising rails. Filtered indexes over a handful of curated rows.
        b.HasIndex(x => new { x.IsFeatured, x.FeaturedOrder })
            .HasFilter("[IsFeatured] = 1 AND [DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Featured");

        b.HasIndex(x => new { x.IsTrending, x.TrendingOrder })
            .HasFilter("[IsTrending] = 1 AND [DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Trending");

        b.HasIndex(x => new { x.IsHero, x.HeroOrder })
            .HasFilter("[IsHero] = 1 AND [DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Hero");

        // In-stock filter.
        b.HasIndex(x => new { x.Status, x.InStock })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Status_InStock");

        // Text search.
        //
        // `Name LIKE '%term%'` has a leading wildcard, so no index can seek — it must scan. The
        // point of this index is to make the scan cheap: without it the optimiser scans the
        // clustered index, dragging every wide column (including an 8,000-character Description)
        // through memory for all 4,000+ rows.
        //
        // Kept to the single Name column on purpose. An earlier version added
        // INCLUDE(ShortDescription) to let the search cover both columns; that tripled the index
        // width and the scan went from ~75 ms to ~280 ms — so the search predicate was narrowed
        // to Name instead. Measure before widening this.
        b.HasIndex(x => x.Name)
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Products_Name_Search");

        b.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("ProductVariants");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        b.Property(x => x.Barcode).HasMaxLength(100);
        b.Property(x => x.Unit).HasMaxLength(30).IsRequired();

        // Optimistic concurrency. Two orders decrementing the same stock row concurrently would
        // otherwise lose one decrement — the classic oversell. A rowversion mismatch surfaces it
        // as a DbUpdateConcurrencyException the checkout service retries.
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Product)
            .WithMany(x => x.Variants)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("IX_ProductVariants_Sku");
        b.HasIndex(x => new { x.ProductId, x.IsActive, x.DisplayOrder }).HasDatabaseName("IX_ProductVariants_Product");
        b.HasIndex(x => x.Barcode).HasDatabaseName("IX_ProductVariants_Barcode");

        // Low-stock report: find tracked variants at or below their threshold.
        b.HasIndex(x => new { x.TrackInventory, x.StockQuantity })
            .HasDatabaseName("IX_ProductVariants_Stock");

        // Products are soft-deleted; without a matching filter here EF warns that a required
        // dependent could reference a filtered principal.
    }
}

public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> b)
    {
        b.ToTable("ProductOptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();

        b.HasOne(x => x.Product)
            .WithMany(x => x.Options)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ProductId, x.DisplayOrder });
    }
}

public class ProductOptionValueConfiguration : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> b)
    {
        b.ToTable("ProductOptionValues");
        b.HasKey(x => x.Id);
        b.Property(x => x.Value).HasMaxLength(200).IsRequired();
        b.Property(x => x.HexColor).HasMaxLength(9);

        b.HasOne(x => x.Option)
            .WithMany(x => x.Values)
            .HasForeignKey(x => x.OptionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.OptionId, x.DisplayOrder });
    }
}

public class VariantOptionValueConfiguration : IEntityTypeConfiguration<VariantOptionValue>
{
    public void Configure(EntityTypeBuilder<VariantOptionValue> b)
    {
        b.ToTable("VariantOptionValues");
        b.HasKey(x => new { x.VariantId, x.OptionValueId });

        b.HasOne(x => x.Variant)
            .WithMany(x => x.OptionValues)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on this leg: cascading from both sides of a join table creates multiple
        // cascade paths, which SQL Server rejects outright.
        b.HasOne(x => x.OptionValue)
            .WithMany(x => x.VariantValues)
            .HasForeignKey(x => x.OptionValueId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImages");
        b.HasKey(x => x.Id);

        b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
        b.Property(x => x.AltText).HasMaxLength(300);
        b.Property(x => x.BlurHash).HasMaxLength(200);

        b.HasOne(x => x.Product)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // ClientSetNull, not SetNull. A product cascades to its images directly *and* through its
        // variants, and SQL Server rejects two cascade paths into the same table (error 1785).
        // The database leg becomes NO ACTION; EF nulls the reference client-side when the variant
        // is loaded, and variant deletion goes through the catalogue service, which reassigns
        // images explicitly.
        b.HasOne(x => x.Variant)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        // Product cards select only the primary image. This index lets that be a seek per product
        // rather than loading the whole image collection and filtering in memory.
        b.HasIndex(x => new { x.ProductId, x.IsPrimary, x.DisplayOrder })
            .HasDatabaseName("IX_ProductImages_Product_Primary");

    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> b)
    {
        b.ToTable("ProductTags");
        b.HasKey(x => new { x.ProductId, x.TagId });

        b.HasOne(x => x.Product)
            .WithMany(x => x.ProductTags)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tag)
            .WithMany(x => x.ProductTags)
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.TagId);
    }
}

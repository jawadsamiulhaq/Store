using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Domain.Catalog;
using Store.Domain.Enums;
using Store.Infrastructure.Catalog;

namespace Store.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a realistic demo catalogue for a Hong Kong provision store.
/// </summary>
/// <remarks>
/// Deliberately generated at the legacy store's scale (~4,200 products) so catalogue performance
/// is measured against a comparable dataset rather than a toy one — a paging query that looks fast
/// over 50 rows proves nothing.
/// <para>
/// The data also corrects the legacy catalogue's quality problems: every product carries a real
/// description rather than the repeated string "Premium quality supermarket grocery item.", every
/// product has a category (legacy had 18 uncategorised), and pack sizes are modelled as genuine
/// variants rather than a <c>unit</c> column reading "piece" for all 4,207 rows.
/// </para>
/// </remarks>
public sealed class CatalogSeeder(StoreDbContext db, ILogger<CatalogSeeder> logger)
{
    /// <summary>Fixed seed: the demo catalogue is identical on every machine, which makes performance runs comparable.</summary>
    private readonly Random _random = new(20260923);

    public async Task SeedAsync(int targetProductCount = 4200, CancellationToken ct = default)
    {
        if (await db.Products.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Catalogue already seeded; skipping");
            return;
        }

        var categories = await SeedCategoriesAsync(ct);
        var brands = await SeedBrandsAsync(ct);
        await SeedProductsAsync(categories, brands, targetProductCount, ct);
    }

    // ==========================================================================================
    // Categories
    // ==========================================================================================

    /// <summary>
    /// Category tree. The top level keeps the legacy store's real sections (Frozen, Dry Food,
    /// Cooking Oil, Lentils / Beans, Ready / Canned Food, Health) so the client recognises their
    /// shop, with subcategories added — legacy had a flat list only.
    /// </summary>
    private static readonly (string Name, string[] Children)[] CategoryTree =
    [
        ("Rice & Grains", ["Basmati Rice", "Sella Rice", "Brown & Red Rice", "Flour & Atta"]),
        ("Lentils & Beans", ["Daal & Lentils", "Chickpeas", "Kidney Beans", "Split Peas"]),
        ("Spices & Masala", ["Whole Spices", "Ground Spices", "Masala Blends", "Salt & Pepper"]),
        ("Cooking Oil & Ghee", ["Sunflower Oil", "Mustard Oil", "Olive Oil", "Desi Ghee"]),
        ("Frozen", ["Frozen Paratha & Roti", "Frozen Vegetables", "Frozen Snacks", "Ice Cream"]),
        ("Ready & Canned Food", ["Canned Vegetables", "Canned Fish", "Instant Meals", "Pickles & Chutney"]),
        ("Noodles & Pasta", ["Instant Noodles", "Vermicelli", "Pasta & Macaroni"]),
        ("Sauces & Condiments", ["Ketchup & Sauces", "Mayonnaise", "Vinegar", "Cooking Paste"]),
        ("Beverages", ["Tea", "Coffee", "Juice & Squash", "Soft Drinks", "Energy Drinks"]),
        ("Dairy & Eggs", ["Milk & Cream", "Butter & Cheese", "Yoghurt", "Eggs"]),
        ("Snacks & Confectionery", ["Biscuits", "Crisps & Namkeen", "Chocolate", "Sweets & Mithai"]),
        ("Bakery", ["Bread", "Rusk & Toast", "Cakes"]),
        ("Dry Fruits & Nuts", ["Almonds & Cashews", "Dates", "Raisins & Seeds"]),
        ("Health & Wellness", ["Supplements", "Honey", "Herbal & Ayurvedic"]),
        ("Household", ["Laundry", "Cleaning", "Kitchen Essentials"]),
        ("Personal Care", ["Hair Care", "Skin Care", "Oral Care", "Soap & Body Wash"]),
        ("Baby Care", ["Baby Food", "Nappies", "Baby Toiletries"])
    ];

    private async Task<List<Category>> SeedCategoriesAsync(CancellationToken ct)
    {
        var all = new List<Category>();
        var order = 0;

        foreach (var (parentName, childNames) in CategoryTree)
        {
            var parentSlug = SlugGenerator.Generate(parentName);

            var parent = new Category
            {
                Name = parentName,
                Slug = parentSlug,
                Description = $"Everything in {parentName.ToLowerInvariant()}, delivered across Hong Kong.",
                Path = $"/{parentSlug}/",
                Depth = 0,
                DisplayOrder = order++,
                IsActive = true,
                ShowInMenu = true,
                MetaTitle = $"{parentName} — Waqas Provision Store",
                MetaDescription = $"Shop {parentName.ToLowerInvariant()} online. Fast delivery across Kowloon, Hong Kong Island and the New Territories."
            };

            all.Add(parent);

            var childOrder = 0;

            foreach (var childName in childNames)
            {
                var childSlug = SlugGenerator.Generate(childName);

                all.Add(new Category
                {
                    Name = childName,
                    Slug = childSlug,
                    Description = $"{childName} from trusted brands.",
                    ParentId = parent.Id,
                    Path = $"/{parentSlug}/{childSlug}/",
                    Depth = 1,
                    DisplayOrder = childOrder++,
                    IsActive = true,
                    ShowInMenu = true,
                    MetaTitle = $"{childName} — Waqas Provision Store"
                });
            }
        }

        db.Categories.AddRange(all);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Count} categories", all.Count);
        return all;
    }

    // ==========================================================================================
    // Brands
    // ==========================================================================================

    private static readonly (string Name, string Country)[] BrandSeed =
    [
        ("Shan", "Pakistan"), ("National", "Pakistan"), ("Ahmed Foods", "Pakistan"),
        ("Laziza", "Pakistan"), ("Mitchell's", "Pakistan"), ("Dalda", "Pakistan"),
        ("Tapal", "Pakistan"), ("Olper's", "Pakistan"), ("K&N's", "Pakistan"),
        ("MDH", "India"), ("Everest", "India"), ("Amul", "India"), ("Haldiram's", "India"),
        ("Tata", "India"), ("Patanjali", "India"), ("Aashirvaad", "India"),
        ("Lee Kum Kee", "Hong Kong"), ("Vita", "Hong Kong"), ("Garden", "Hong Kong"),
        ("Nissin", "Japan"), ("Kikkoman", "Japan"), ("Ajinomoto", "Japan"),
        ("Nestlé", "Switzerland"), ("Maggi", "Switzerland"), ("Knorr", "Netherlands"),
        ("Unilever", "United Kingdom"), ("Kellogg's", "United States"), ("Heinz", "United States"),
        ("Barilla", "Italy"), ("Borges", "Spain"), ("Al Ain", "United Arab Emirates"),
        ("Tilda", "United Kingdom"), ("Falak", "Pakistan"), ("Guard", "Pakistan"),
        ("Sunridge", "Pakistan"), ("Rafhan", "Pakistan"), ("Young's", "Pakistan"),
        ("Colgate", "United States"), ("Dettol", "United Kingdom"), ("Surf Excel", "India")
    ];

    private async Task<List<Brand>> SeedBrandsAsync(CancellationToken ct)
    {
        var brands = BrandSeed.Select((b, i) => new Brand
        {
            Name = b.Name,
            Slug = SlugGenerator.Generate(b.Name),
            CountryOfOrigin = b.Country,
            Description = $"{b.Name} products, imported from {b.Country}.",
            DisplayOrder = i,
            IsActive = true,
            IsFeatured = i < 8
        }).ToList();

        db.Brands.AddRange(brands);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Count} brands", brands.Count);
        return brands;
    }

    // ==========================================================================================
    // Products
    // ==========================================================================================

    /// <summary>Item names per top-level category, combined with brands and pack sizes to reach scale.</summary>
    private static readonly Dictionary<string, string[]> ItemsByCategory = new()
    {
        ["Rice & Grains"] = ["Basmati Rice", "Sella Basmati Rice", "Super Kernel Rice", "Brown Rice", "Red Rice", "Chapati Atta", "Maida Flour", "Besan Gram Flour", "Semolina Suji", "Corn Flour"],
        ["Lentils & Beans"] = ["Masoor Daal", "Moong Daal", "Chana Daal", "Toor Daal", "Urad Daal", "White Chickpeas", "Black Chickpeas", "Red Kidney Beans", "White Beans", "Green Peas Split"],
        ["Spices & Masala"] = ["Red Chilli Powder", "Turmeric Powder", "Coriander Powder", "Cumin Seeds", "Black Pepper", "Garam Masala", "Biryani Masala", "Karahi Masala", "Chaat Masala", "Cardamom Green", "Cinnamon Sticks", "Bay Leaves", "Cloves", "Fennel Seeds", "Mustard Seeds"],
        ["Cooking Oil & Ghee"] = ["Sunflower Cooking Oil", "Canola Oil", "Mustard Oil", "Extra Virgin Olive Oil", "Banaspati Ghee", "Desi Ghee", "Corn Oil", "Sesame Oil"],
        ["Frozen"] = ["Frozen Paratha", "Frozen Chapati", "Frozen Samosa", "Frozen Spring Roll", "Frozen Seekh Kebab", "Frozen Chicken Nuggets", "Frozen Mixed Vegetables", "Frozen Peas", "Frozen Corn", "Vanilla Ice Cream", "Kulfi"],
        ["Ready & Canned Food"] = ["Canned Chickpeas", "Canned Sweet Corn", "Canned Tuna", "Canned Sardines", "Baked Beans", "Tomato Paste", "Mango Pickle", "Mixed Pickle", "Lime Pickle", "Ready Biryani", "Ready Haleem"],
        ["Noodles & Pasta"] = ["Instant Noodles Chicken", "Instant Noodles Masala", "Cup Noodles", "Vermicelli Seviyan", "Spaghetti", "Penne Pasta", "Macaroni"],
        ["Sauces & Condiments"] = ["Tomato Ketchup", "Chilli Garlic Sauce", "Soy Sauce", "Oyster Sauce", "Mayonnaise", "Garlic Mayo", "White Vinegar", "Apple Cider Vinegar", "Ginger Garlic Paste", "Tamarind Paste"],
        ["Beverages"] = ["Black Tea Leaves", "Green Tea Bags", "Kashmiri Pink Tea", "Instant Coffee", "Mango Juice", "Orange Juice", "Rooh Afza Squash", "Cola Soft Drink", "Lemon Soda", "Energy Drink"],
        ["Dairy & Eggs"] = ["Full Cream Milk", "UHT Milk", "Fresh Cream", "Salted Butter", "Unsalted Butter", "Cheddar Cheese", "Mozzarella Cheese", "Plain Yoghurt", "Greek Yoghurt", "Farm Eggs"],
        ["Snacks & Confectionery"] = ["Digestive Biscuits", "Cream Biscuits", "Salted Crackers", "Potato Crisps", "Bhujia Namkeen", "Dal Moth", "Milk Chocolate", "Dark Chocolate", "Gulab Jamun Tin", "Soan Papdi"],
        ["Bakery"] = ["White Sandwich Bread", "Brown Bread", "Milk Rusk", "Bakery Toast", "Plain Cake", "Fruit Cake"],
        ["Dry Fruits & Nuts"] = ["Almonds", "Cashew Nuts", "Pistachios", "Walnuts", "Dried Dates", "Ajwa Dates", "Golden Raisins", "Black Raisins", "Sunflower Seeds", "Chia Seeds"],
        ["Health & Wellness"] = ["Multivitamin Tablets", "Vitamin C Tablets", "Natural Honey", "Sidr Honey", "Isabgol Husk", "Herbal Tea", "Apple Cider Capsules"],
        ["Household"] = ["Washing Powder", "Liquid Detergent", "Dishwashing Liquid", "Floor Cleaner", "Glass Cleaner", "Bleach", "Aluminium Foil", "Cling Film", "Garbage Bags", "Kitchen Towel"],
        ["Personal Care"] = ["Anti Dandruff Shampoo", "Herbal Shampoo", "Hair Oil", "Face Wash", "Moisturising Cream", "Toothpaste", "Toothbrush", "Bath Soap", "Body Wash", "Hand Sanitiser"],
        ["Baby Care"] = ["Infant Formula Stage 1", "Infant Formula Stage 2", "Baby Cereal", "Baby Nappies Medium", "Baby Nappies Large", "Baby Wipes", "Baby Shampoo", "Baby Lotion"]
    };

    /// <summary>Pack sizes per category, expressed as real variants — the thing the legacy store could not represent.</summary>
    private static readonly Dictionary<string, (string Label, string Unit, decimal Value, decimal Multiplier)[]> PackSizes = new()
    {
        ["Rice & Grains"] = [("1 kg", "kg", 1, 1m), ("5 kg", "kg", 5, 4.6m), ("10 kg", "kg", 10, 8.8m), ("25 kg", "kg", 25, 21m)],
        ["Lentils & Beans"] = [("500 g", "g", 500, 1m), ("1 kg", "kg", 1, 1.9m), ("2 kg", "kg", 2, 3.6m)],
        ["Spices & Masala"] = [("50 g", "g", 50, 1m), ("100 g", "g", 100, 1.85m), ("200 g", "g", 200, 3.5m), ("400 g", "g", 400, 6.6m)],
        ["Cooking Oil & Ghee"] = [("1 L", "litre", 1, 1m), ("3 L", "litre", 3, 2.85m), ("5 L", "litre", 5, 4.6m)],
        ["Frozen"] = [("400 g", "g", 400, 1m), ("800 g", "g", 800, 1.9m)],
        ["Ready & Canned Food"] = [("400 g", "g", 400, 1m), ("800 g", "g", 800, 1.85m)],
        ["Noodles & Pasta"] = [("Single pack", "piece", 1, 1m), ("Pack of 5", "pack", 5, 4.7m), ("Pack of 12", "pack", 12, 11m)],
        ["Sauces & Condiments"] = [("300 ml", "ml", 300, 1m), ("500 ml", "ml", 500, 1.6m), ("1 L", "litre", 1, 3m)],
        ["Beverages"] = [("250 ml", "ml", 250, 1m), ("500 ml", "ml", 500, 1.8m), ("1 L", "litre", 1, 3.2m), ("1.5 L", "litre", 1.5m, 4.5m)],
        ["Dairy & Eggs"] = [("250 ml", "ml", 250, 1m), ("500 ml", "ml", 500, 1.9m), ("1 L", "litre", 1, 3.6m)],
        ["Snacks & Confectionery"] = [("Small", "piece", 1, 1m), ("Family pack", "pack", 1, 2.4m)],
        ["Bakery"] = [("Regular", "piece", 1, 1m), ("Large", "piece", 1, 1.6m)],
        ["Dry Fruits & Nuts"] = [("250 g", "g", 250, 1m), ("500 g", "g", 500, 1.9m), ("1 kg", "kg", 1, 3.7m)],
        ["Health & Wellness"] = [("Small", "piece", 1, 1m), ("Large", "piece", 1, 1.8m)],
        ["Household"] = [("500 g", "g", 500, 1m), ("1 kg", "kg", 1, 1.85m), ("3 kg", "kg", 3, 5.2m)],
        ["Personal Care"] = [("Small", "piece", 1, 1m), ("Medium", "piece", 1, 1.7m), ("Large", "piece", 1, 2.6m)],
        ["Baby Care"] = [("Small", "piece", 1, 1m), ("Large", "piece", 1, 2.2m)]
    };

    private async Task SeedProductsAsync(
        List<Category> categories, List<Brand> brands, int target, CancellationToken ct)
    {
        var leafByParent = categories
            .Where(c => c.Depth == 1)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var parents = categories.Where(c => c.Depth == 0).ToList();

        var products = new List<Product>(target);
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var usedSkus = new HashSet<string>(StringComparer.Ordinal);
        var sequence = 1000;

        // Cycles brand × item × pack range until the target count is reached, which produces a
        // realistic long tail rather than a handful of repeated rows.
        while (products.Count < target)
        {
            foreach (var parent in parents)
            {
                if (products.Count >= target) break;
                if (!ItemsByCategory.TryGetValue(parent.Name, out var items)) continue;

                var leaves = leafByParent.GetValueOrDefault(parent.Id, [parent]);
                var packs = PackSizes.GetValueOrDefault(parent.Name, [("Standard", "piece", 1, 1m)]);

                foreach (var item in items)
                {
                    if (products.Count >= target) break;

                    var brand = brands[_random.Next(brands.Count)];
                    var leaf = leaves[_random.Next(leaves.Count)];
                    var name = $"{brand.Name} {item}";

                    // Disambiguate the repeats that scale forces, without the legacy store's
                    // random numeric slug suffix.
                    var slug = SlugGenerator.Generate(name);

                    if (!usedSlugs.Add(slug))
                    {
                        var variantWord = VariantWords[_random.Next(VariantWords.Length)];
                        name = $"{brand.Name} {variantWord} {item}";
                        slug = SlugGenerator.Generate(name);

                        var attempt = 2;
                        while (!usedSlugs.Add(slug))
                        {
                            slug = SlugGenerator.Generate($"{name}-{attempt++}");
                        }
                    }

                    var basePrice = Math.Round((decimal)(_random.NextDouble() * 95 + 8), 1);
                    var onSale = _random.Next(100) < 18;
                    var packCount = Math.Min(packs.Length, _random.Next(1, packs.Length + 1));

                    var product = new Product
                    {
                        Name = name,
                        Slug = slug,
                        ShortDescription = BuildShortDescription(item, brand.Name, parent.Name),
                        Description = BuildDescription(item, brand.Name, brand.CountryOfOrigin!, parent.Name),
                        CategoryId = leaf.Id,
                        BrandId = brand.Id,
                        Status = ProductStatus.Active,
                        PublishedAt = DateTimeOffset.UtcNow.AddDays(-_random.Next(1, 400)),
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-_random.Next(1, 400)),
                        IsFeatured = _random.Next(100) < 3,
                        IsTrending = _random.Next(100) < 3,
                        IsHero = false,
                        Badge = onSale ? "Sale" : _random.Next(100) < 5 ? "New" : null,
                        SalesCount = _random.Next(0, 500),
                        ViewCount = _random.Next(0, 5000),
                        MetaTitle = $"{name} — buy online in Hong Kong",
                        MetaDescription = $"Order {name} for delivery across Hong Kong. Genuine {brand.Name} product."
                    };

                    for (var i = 0; i < packCount; i++)
                    {
                        var pack = packs[i];
                        var price = Math.Round(basePrice * pack.Multiplier, 1);

                        var sku = $"{Abbreviate(brand.Name)}-{Abbreviate(item)}-{sequence++}";
                        while (!usedSkus.Add(sku))
                        {
                            sku = $"{Abbreviate(brand.Name)}-{Abbreviate(item)}-{sequence++}";
                        }

                        // A deliberate slice is out of stock, so the in-stock filter and the
                        // "sold out" UI have something real to act on.
                        var stock = _random.Next(100) < 8 ? 0 : _random.Next(3, 260);

                        product.Variants.Add(new ProductVariant
                        {
                            Name = packCount > 1 ? pack.Label : null,
                            Sku = sku,
                            Price = price,
                            CompareAtPrice = onSale ? Math.Round(price * (1 + (decimal)(_random.NextDouble() * 0.35 + 0.1)), 1) : null,
                            CostPrice = Math.Round(price * 0.72m, 2),
                            StockQuantity = stock,
                            LowStockThreshold = 10,
                            TrackInventory = true,
                            Unit = pack.Unit,
                            UnitValue = pack.Value,
                            WeightGrams = EstimateWeightGrams(pack.Unit, pack.Value),
                            IsDefault = i == 0,
                            IsActive = true,
                            DisplayOrder = i
                        });
                    }

                    ProductService.RecalculateAggregates(product);

                    product.Images.Add(new ProductImage
                    {
                        // Deterministic local placeholder path. Real photography replaces these via
                        // the media library; the point here is that dimensions and a placeholder
                        // exist so layout is reserved and CLS stays at zero.
                        Url = $"/images/products/{slug}.webp",
                        ThumbnailUrl = $"/images/products/{slug}-thumb.webp",
                        AltText = name,
                        Width = 800,
                        Height = 800,
                        BlurHash = null,
                        IsPrimary = true,
                        DisplayOrder = 0
                    });

                    products.Add(product);
                }
            }
        }

        // Inserted in batches. One SaveChanges over 4,200 products with their variants and images
        // builds an enormous single transaction and a change-tracker graph to match.
        const int batchSize = 400;

        for (var offset = 0; offset < products.Count; offset += batchSize)
        {
            var batch = products.Skip(offset).Take(batchSize).ToList();
            db.Products.AddRange(batch);
            await db.SaveChangesAsync(ct);

            // Detach the saved batch so the change tracker does not grow across the whole run.
            foreach (var entry in db.ChangeTracker.Entries().ToList())
            {
                entry.State = EntityState.Detached;
            }

            logger.LogInformation("Seeded {Done}/{Total} products", Math.Min(offset + batchSize, products.Count), products.Count);
        }

        await RefreshCountsAsync(ct);

        logger.LogInformation(
            "Catalogue seeded: {Products} products, {Variants} variants",
            products.Count, products.Sum(p => p.Variants.Count));
    }

    /// <summary>Sets the maintained category and brand counts once, after the bulk insert.</summary>
    private async Task RefreshCountsAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE b
            SET ProductCount = x.Cnt
            FROM Brands b
            JOIN (
                SELECT BrandId, COUNT(*) AS Cnt
                FROM Products
                WHERE Status = 1 AND DeletedAt IS NULL AND BrandId IS NOT NULL
                GROUP BY BrandId
            ) x ON x.BrandId = b.Id;

            UPDATE c
            SET ProductCount = x.Cnt
            FROM Categories c
            JOIN (
                SELECT c2.Id, COUNT(p.Id) AS Cnt
                FROM Categories c2
                LEFT JOIN Categories d ON d.Path LIKE c2.Path + '%'
                LEFT JOIN Products p ON p.CategoryId = d.Id AND p.Status = 1 AND p.DeletedAt IS NULL
                GROUP BY c2.Id
            ) x ON x.Id = c.Id;
            """, ct);
    }

    private static readonly string[] VariantWords =
        ["Premium", "Classic", "Gold", "Select", "Special", "Extra", "Pure", "Royal", "Fresh", "Authentic"];

    private static string BuildShortDescription(string item, string brand, string category) =>
        $"{item} from {brand}. A {category.ToLowerInvariant()} staple, stocked fresh at Ngau Chi Wan Market.";

    /// <summary>
    /// Builds a varied description. Not the legacy store's single repeated sentence across all
    /// 4,207 products, which made the catalogue read as unfinished.
    /// </summary>
    private string BuildDescription(string item, string brand, string country, string category)
    {
        string[] openers =
        [
            $"<p>{brand} {item} is a kitchen staple trusted in homes across Hong Kong.</p>",
            $"<p>Genuine {brand} {item}, imported directly from {country}.</p>",
            $"<p>Stock up on {brand} {item} — one of our most-requested {category.ToLowerInvariant()} lines.</p>"
        ];

        string[] bodies =
        [
            "<p>Sealed for freshness and stored in the right conditions from arrival to delivery.</p>",
            "<p>Sourced through established importers, so quality and batch consistency stay reliable.</p>",
            "<p>Available in several pack sizes, so you can buy for a single household or in bulk.</p>"
        ];

        const string closer =
            "<p>Delivered across Kowloon, Hong Kong Island and the New Territories, or collect in store at Ngau Chi Wan Market, Choi Hung.</p>";

        return openers[_random.Next(openers.Length)] + bodies[_random.Next(bodies.Length)] + closer;
    }

    private static decimal? EstimateWeightGrams(string unit, decimal value) => unit switch
    {
        "kg" => value * 1000,
        "g" => value,
        "litre" => value * 1000,
        "ml" => value,
        _ => 500
    };

    private static string Abbreviate(string value)
    {
        var letters = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.Length <= 3 ? letters : letters[..3];
    }
}

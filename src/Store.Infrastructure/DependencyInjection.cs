using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Store.Application.Common;
using Store.Domain.Identity;
using Store.Infrastructure.Caching;
using Store.Infrastructure.Catalog;
using Store.Infrastructure.Commerce;
using Store.Infrastructure.Identity;
using Store.Infrastructure.Persistence;
using Store.Infrastructure.Persistence.Interceptors;
using Store.Infrastructure.Persistence.Seed;
using Store.Infrastructure.Services;

namespace Store.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddIdentityServices(configuration);
        services.AddCaching();
        services.AddEmail(configuration);
        services.AddFileStorage(configuration);

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddScoped<IdentitySeeder>();
        services.AddScoped<PlatformSeeder>();
        services.AddScoped<CatalogSeeder>();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        // Singleton, because the pooled DbContext's options are resolved from the root provider —
        // a scoped interceptor throws there. It is stateless and reads request identity through
        // IAuditContext, which is itself singleton-safe.
        services.AddSingleton<AuditingInterceptor>();

        // Pooled: reusing context instances avoids re-running model validation and internal
        // service-provider construction on every request. On a read-heavy catalogue this is a
        // measurable slice of per-request time, and it costs nothing in correctness because the
        // pool resets state between uses.
        services.AddDbContextPool<StoreDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(StoreDbContext).Assembly.FullName);

                // Retries transient faults (failover, throttling, transport blips) instead of
                // surfacing them to the shopper as a failed checkout.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);

                // A query that has not answered in 30 s will not become useful by waiting longer;
                // failing fast keeps a bad query from occupying the connection pool.
                sql.CommandTimeout(30);
            });

            // Tracking is deliberately left ON as the default, and reads opt out with
            // AsNoTracking() individually.
            //
            // A global NoTracking default looks like a free performance win and is a trap: every
            // load-then-mutate-then-SaveChanges path silently persists nothing, because the
            // entity was never tracked. There is no error — the write just vanishes. This was not
            // hypothetical here; it disabled refresh-token revocation until it was caught.
            //
            // The win was illusory in any case: the hot read paths all project to DTOs with
            // Select(), and EF never tracks a projection to a non-entity type. So the queries that
            // actually matter for throughput were never paying for tracking to begin with.
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddHealthChecks()
            .AddDbContextCheck<StoreDbContext>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        return services;
    }

    private static IServiceCollection AddIdentityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey),
                "Jwt:SigningKey is not configured. Set it via user-secrets in development or an " +
                "environment variable in production.")
            .Validate(
                // HMAC-SHA256 needs at least 256 bits of key. A shorter key throws deep inside
                // the token handler at first sign-in; validating here fails at startup instead.
                o => o.SigningKey.Length >= 32,
                "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length carries far more strength than symbol classes, and complexity rules push
                // people towards predictable substitutions. Identity still hashes with PBKDF2 at
                // its configured iteration count.
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // Throttles online password guessing. Applies to the account, so it cannot be
                // sidestepped by rotating source IPs.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<StoreDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IProductService, ProductService>();

        // Shared by wishlist, addresses, cart, checkout, orders and reviews — every one of which
        // used to resolve the customer profile itself and treat "no profile" as "not signed in".
        services.AddScoped<ICustomerContext, CustomerContext>();

        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<Reviews.IReviewService, Reviews.ReviewService>();
        services.AddScoped<Inventory.IInventoryService, Inventory.InventoryService>();
        services.AddScoped<Reporting.IReportingService, Reporting.ReportingService>();

        services.AddScoped<Platform.ICouponAdminService, Platform.CouponAdminService>();
        services.AddScoped<Platform.ICustomerAdminService, Platform.CustomerAdminService>();
        services.AddScoped<Platform.ISettingsService, Platform.SettingsService>();
        services.AddScoped<Platform.IContentService, Platform.ContentService>();
        services.AddScoped<Platform.IAuditService, Platform.AuditService>();

        return services;
    }

    private static IServiceCollection AddCaching(this IServiceCollection services)
    {
#pragma warning disable EXTEXP0018 // HybridCache is still marked experimental in this release.
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };

            // Caps what a single entry may occupy, so one oversized payload cannot evict the
            // whole working set.
            options.MaximumPayloadBytes = 1024 * 1024;
            options.MaximumKeyLength = 512;
        });
#pragma warning restore EXTEXP0018

        services.AddSingleton<ICacheService, HybridCacheService>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations and runs the idempotent seeders.
    /// </summary>
    /// <remarks>
    /// Migrating on startup suits development and a single-instance deployment. On a multi-
    /// instance production rollout, run <c>dotnet ef database update</c> (or a generated SQL
    /// script) as a deployment step instead, so concurrent instances cannot race each other —
    /// hence <paramref name="applyMigrations"/>.
    /// </remarks>
    public static async Task InitialiseDatabaseAsync(
        this IServiceProvider services,
        bool applyMigrations,
        bool seedDemoCatalogue = false,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        if (applyMigrations)
        {
            var db = provider.GetRequiredService<StoreDbContext>();
            await db.Database.MigrateAsync(ct);
        }

        await provider.GetRequiredService<IdentitySeeder>().SeedAsync(ct);
        await provider.GetRequiredService<PlatformSeeder>().SeedAsync(ct);

        // Opt-in and development-only. A demo catalogue appearing in a client's live store would
        // be a serious embarrassment, so this never runs unless explicitly switched on.
        if (seedDemoCatalogue)
        {
            await provider.GetRequiredService<CatalogSeeder>().SeedAsync(ct: ct);
        }
    }
}


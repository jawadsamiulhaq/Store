using System.IO.Compression;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Store.Api.Authorization;
using Store.Api.Endpoints;
using Store.Api.Middleware;
using Store.Application.Common;
using Store.Infrastructure;
using Store.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Store.Api")
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/store-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        // Buffered writes keep file logging off the request's critical path.
        buffered: true,
        flushToDiskInterval: TimeSpan.FromSeconds(2)));

// ---------------------------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();

// Singleton: stateless, reads the ambient request through IHttpContextAccessor. Must be
// singleton so the pooled DbContext's auditing interceptor can depend on it.
builder.Services.AddSingleton<IAuditContext, AuditContext>();

// Scoped: needs IPermissionService, which needs the scoped DbContext.
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.Configure<PerformanceOptions>(
    builder.Configuration.GetSection(PerformanceOptions.SectionName));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------------------------------------------------------------------------------------------
// Authentication & authorization
// ---------------------------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var signingKey = jwtSection["SigningKey"];

if (string.IsNullOrWhiteSpace(signingKey))
{
    // Failing at startup is the correct behaviour: an API that boots without a signing key
    // cannot verify anything, and discovering that at the first sign-in is far worse.
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set it with " +
        "`dotnet user-secrets set \"Jwt:SigningKey\" \"<64+ random characters>\"` in development, " +
        "or the Jwt__SigningKey environment variable in production.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),

            // Default is five minutes of grace, which quietly extends a 15-minute token to 20.
            ClockSkew = TimeSpan.Zero
        };

        // Lets the client distinguish "token expired, refresh and retry" from "not allowed",
        // so a silent refresh can happen without bouncing the user to the sign-in page.
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    context.Response.Headers["X-Token-Expired"] = "true";
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        // An explicit allowlist, never AllowAnyOrigin: the refresh token travels as a cookie, and
        // credentialed requests cannot use a wildcard origin anyway.
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithExposedHeaders("X-Correlation-Id", "X-Token-Expired", "Content-Disposition"));
});

// ---------------------------------------------------------------------------------------------
// Performance: compression, output caching, rate limiting
// ---------------------------------------------------------------------------------------------
builder.Services.AddResponseCompression(options =>
{
    // Safe here because the API serves JSON, not HTML with embedded secrets, and responses are
    // not attacker-controlled reflections of a secret — the conditions behind BREACH.
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "image/svg+xml"]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

builder.Services.AddOutputCache(options =>
{
    // Anonymous catalogue reads. Varying by query string is what makes a cached page correct
    // across filter, sort and pagination combinations.
    options.AddPolicy("catalog", policy => policy
        .Expire(TimeSpan.FromMinutes(2))
        .SetVaryByQuery("*")
        .Tag("catalog"));

    options.AddPolicy("reference", policy => policy
        .Expire(TimeSpan.FromMinutes(15))
        .Tag("reference"));
});

var rateLimiting = builder.Configuration.GetSection("RateLimiting");
var globalPermit = rateLimiting.GetValue("GlobalPermitPerMinute", 300);
var authPermit = rateLimiting.GetValue("AuthPermitPerMinute", 10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned per authenticated user, falling back to IP for anonymous traffic, so one
    // aggressive client cannot consume the budget for everyone behind the same NAT.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.IsAuthenticated == true
                ? context.User.Identity.Name ?? "authenticated"
                : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Sign-in and password-reset get a far tighter budget — this is the anti-automation control
    // that complements Identity's per-account lockout.
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------------------------------------------------------------------------------------------
// API surface
// ---------------------------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Omitting nulls is a real payload saving across a catalogue response with many optional
    // fields, and camelCase matches what the TypeScript client expects.
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Behind nginx the client IP and scheme arrive in headers; without this, audit records log
    // the proxy and redirect URIs come out as http.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Cleared because the reverse proxy sits on an address not known at build time. In production
    // behind a fixed nginx or load balancer, add that proxy to KnownIPNetworks/KnownProxies
    // instead — an empty allowlist means a client could spoof X-Forwarded-For if the API is ever
    // exposed directly rather than only through the proxy.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Pipeline. Order matters throughout.
// ---------------------------------------------------------------------------------------------
app.UseForwardedHeaders();
app.UseExceptionHandler();

// First, so that everything downstream — including the exception handler's response — carries
// the correlation id and is included in the timing.
app.UseRequestTracking();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Waqas Provision Store API")
        .WithTheme(ScalarTheme.BluePlanet));
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    // Defence in depth. The API returns JSON rather than markup, but these cost nothing and
    // close off content-sniffing and framing of any endpoint that does return a document.
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    await next();
});

app.UseResponseCompression();
app.UseCors();
app.UseRateLimiter();
app.UseOutputCache();

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------------------------
app.MapAuthEndpoints();
app.MapAdminIdentityEndpoints();
app.MapCatalogEndpoints();
app.MapCommerceEndpoints();
app.MapOperationsEndpoints();
app.MapPlatformEndpoints();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    name = "Waqas Provision Store API",
    status = "running",
    environment = app.Environment.EnvironmentName
})).ExcludeFromDescription();

// ---------------------------------------------------------------------------------------------
// Startup: migrate and seed
// ---------------------------------------------------------------------------------------------
try
{
    // Auto-migrate in development only. A multi-instance production rollout runs migrations as a
    // deployment step so concurrent instances cannot race one another.
    await app.Services.InitialiseDatabaseAsync(
        applyMigrations: app.Environment.IsDevelopment(),

        // Demo catalogue is opt-in via Seed:DemoCatalogue and only ever in development, so a
        // client's live store can never be populated with generated products.
        seedDemoCatalogue: app.Environment.IsDevelopment()
                           && builder.Configuration.GetValue("Seed:DemoCatalogue", false));
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database initialisation failed");
    throw;
}

app.Run();

/// <summary>Exposed so an integration-test host can reference the entry point assembly.</summary>
public partial class Program;


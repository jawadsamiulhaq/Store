using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Store.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="StoreDbContext"/> for <c>dotnet ef</c> at design time.
/// </summary>
/// <remarks>
/// Without this, EF tooling has to boot the whole API host to find the context — which drags in
/// authentication, caching and the auditing interceptor's <c>ICurrentUser</c>, none of which can
/// be resolved outside a request. This factory builds the model and nothing else, so
/// <c>migrations add</c> stays fast and cannot fail for reasons unrelated to the schema.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Store.Api");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.Exists(basePath) ? basePath : Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=WaqasProvisionStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=false";

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(StoreDbContext).Assembly.FullName))
            .Options;

        return new StoreDbContext(options);
    }
}

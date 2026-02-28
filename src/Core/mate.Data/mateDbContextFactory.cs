using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace mate.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>.
/// Reads the connection string from appsettings.json or env var MATE_DB.
/// No tenant context is injected — migrations run without row-level filters.
/// </summary>
public sealed class mateDbContextFactory : IDesignTimeDbContextFactory<mateDbContext>
{
    public mateDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "MATE_")
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? config["DB"]
            ?? "Data Source=mate-local.db";

        var optionsBuilder = new DbContextOptionsBuilder<mateDbContext>();

        if (connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseSqlite(connectionString);
        }
        else
        {
            optionsBuilder.UseNpgsql(connectionString);
        }

        // No tenant context for migrations
        return new mateDbContext(optionsBuilder.Options, tenantContext: null);
    }
}

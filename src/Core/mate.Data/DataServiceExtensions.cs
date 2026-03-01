using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Data;

/// <summary>
/// Extension methods for registering the mate.Data layer in the DI container.
/// Call one of these from the host's Program.cs or the AddmateCore extension.
/// </summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// Registers <see cref="mateDbContext"/> using SQLite.
    /// Suitable for Phase 1 (local) deployments.
    /// </summary>
    public static IServiceCollection AddmateSqlite(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? config["MATE_DB"]
            ?? "Data Source=mate-local.db";

        services.AddDbContext<mateDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(mateDbContext).Assembly.FullName));
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="mateDbContext"/> using PostgreSQL.
    /// Suitable for Phase 1 (self-hosted Postgres) or Phase 2 (Azure Database for Postgres).
    /// </summary>
    public static IServiceCollection AddmatePostgres(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? config["MATE_DB"]
            ?? throw new InvalidOperationException(
                "No database connection string configured. " +
                "Set ConnectionStrings__Default or MATE_DB environment variable.");

        services.AddDbContext<mateDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(mateDbContext).Assembly.FullName));
        });

        return services;
    }

    /// <summary>
    /// Applies any pending EF migrations at startup and optionally seeds the database.
    /// Call from Program.cs after the service container is built, before app.Run().
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, bool seed = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<mateDbContext>();

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // Log but don't crash — allows the app to start when DB is temporarily unavailable
            var logger = scope.ServiceProvider
                .GetService<ILogger<mateDbContext>>();
            logger?.LogError(ex, "Database migration failed. The application may not function correctly.");
            throw;
        }

        if (seed)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<mateDbSeeder>();
            await seeder.SeedAsync();
        }
    }
}

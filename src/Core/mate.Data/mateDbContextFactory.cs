// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
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
            .AddEnvironmentVariables()          // reads ConnectionStrings__Default etc.
            .AddEnvironmentVariables(prefix: "MATE_")
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? config["DB"]
            ?? throw new InvalidOperationException(
                "No database connection string configured. "
                + "Set ConnectionStrings__Default or ConnectionStrings__DB environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<mateDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        // No tenant context for migrations
        return new mateDbContext(optionsBuilder.Options, tenantContext: null);
    }
}

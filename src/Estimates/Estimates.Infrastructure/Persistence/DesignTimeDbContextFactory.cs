using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Estimates.Infrastructure.Persistence;

/// <summary>
/// Used only by the "dotnet ef" tool to create migrations. Creating a migration does not connect
/// to the database, so a placeholder connection string without credentials is enough.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EstimatesDbContext>
{
    public EstimatesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EstimatesDbContext>()
            .UseNpgsql("Host=localhost;Database=estimates", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .Options;

        return new EstimatesDbContext(options);
    }
}

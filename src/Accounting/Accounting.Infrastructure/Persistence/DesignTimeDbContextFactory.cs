using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// Used only by the "dotnet ef" tool to create migrations. Creating a migration does not connect
/// to the database, so a placeholder connection string without credentials is enough.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql("Host=localhost;Database=accounting", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .Options;

        return new AccountingDbContext(options);
    }
}

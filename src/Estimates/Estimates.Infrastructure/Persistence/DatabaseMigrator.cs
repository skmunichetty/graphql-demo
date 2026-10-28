using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Estimates.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations. Used two ways:
/// - "--migrate-database": a one-off deployment step (for example an ECS task) that migrates and
///   exits. Its exit code tells the pipeline whether it is safe to roll out the new API version.
/// - Hosting:ApplyMigrationsOnStartup: local development convenience only.
///
/// EF Core takes a database lock while migrating, so two runs at once cannot both apply a migration.
/// Migrations already applied are skipped, so running it again (or running an older image during a
/// rollback) changes nothing.
/// </summary>
public static class DatabaseMigrator
{
    public const string Argument = "--migrate-database";

    public static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(Argument, StringComparer.Ordinal);
    }

    /// <summary>Returns a process exit code: 0 when the database is up to date, 1 on failure.</summary>
    public static async Task<int> MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator).FullName!);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        EstimatesDbContext dbContext = scope.ServiceProvider.GetRequiredService<EstimatesDbContext>();

        try
        {
            List<string> pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                logger.LogInformation("Database schema is up to date; no migrations to apply");
                return 0;
            }

            logger.LogInformation("Applying {MigrationCount} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await dbContext.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrations applied successfully");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Log the failure type and message only; connection strings are never logged.
            logger.LogError(exception, "Database migration failed");
            return 1;
        }
    }
}

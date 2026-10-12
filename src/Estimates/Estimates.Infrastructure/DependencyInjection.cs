using Estimates.Application;
using Estimates.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estimates.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence. Lifetimes:
    /// - The DbContext factory is a singleton (it only holds options).
    /// - EstimatesDbContext is scoped: one per request, used by the write-side repository.
    /// - The repository is scoped because it holds the scoped DbContext.
    /// - The read store is scoped for simplicity; it creates its own context per call.
    /// </summary>
    public static IServiceCollection AddEstimatesInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // AddDbContextFactory registers both IDbContextFactory<T> (singleton) and T itself (scoped).
        services.AddDbContextFactory<EstimatesDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history");

                // Retries transient connection failures (for example a database restart) with backoff.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        services.AddScoped<IEstimateRepository, EstimateRepository>();
        services.AddScoped<IEstimateReadStore, EstimateReadStore>();
        return services;
    }
}

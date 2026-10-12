using Accounting.Application;
using Accounting.Infrastructure.EstimatesService;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Accounting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and accounting providers. Options classes are bound and validated by
    /// the API host. Lifetimes:
    /// - DbContext factory: singleton; AccountingDbContext and repositories: scoped (one per request).
    /// - Providers: singleton (stateless apart from the mock's in-memory store).
    /// </summary>
    public static IServiceCollection AddAccountingInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<AccountingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        services.AddScoped<IEstimateExportRepository, EstimateExportRepository>();
        services.AddScoped<IEstimateExportReadStore, EstimateExportReadStore>();

        // To add Xero later: implement IAccountingProvider in XeroAccountingProvider and register it here.
        services.AddSingleton<IAccountingProvider, MockAccountingProvider>();
        services.AddSingleton<AccountingProviderRegistry>();
        return services;
    }

    /// <summary>
    /// Registers the typed HttpClient for the Estimates service. Returns the builder so the host can
    /// add web-specific handlers (such as correlation ID propagation).
    ///
    /// Outgoing call behaviour (deliberate):
    /// - Each attempt times out after AttemptTimeoutSeconds; the whole call after TotalTimeoutSeconds.
    /// - Transient failures (network errors, HTTP 5xx, 408, 429) are retried up to MaxRetryAttempts times
    ///   with exponential backoff and jitter. Retrying is safe because the call is a read-only query.
    /// - A circuit breaker stops calling for a while if most calls fail, so a struggling Estimates
    ///   service is not overwhelmed.
    /// </summary>
    public static IHttpClientBuilder AddEstimatesServiceClient(this IServiceCollection services)
    {
        services.AddSingleton<ClientCredentialsTokenProvider>();
        services.AddTransient<ServiceAuthenticationHandler>();

        // Token requests get their own client with a short timeout and a single retry.
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                options.Retry.MaxRetryAttempts = 1;
            });

        IHttpClientBuilder builder = services
            .AddHttpClient<IEstimatesServiceClient, EstimatesServiceClient>((serviceProvider, httpClient) =>
            {
                EstimatesServiceOptions options = serviceProvider.GetRequiredService<IOptions<EstimatesServiceOptions>>().Value;
                httpClient.BaseAddress = new Uri(options.BaseAddress);
            })
            .AddHttpMessageHandler<ServiceAuthenticationHandler>();

        builder.AddResilienceHandler("estimates-service", (pipeline, context) =>
        {
            EstimatesServiceOptions options = context.ServiceProvider.GetRequiredService<IOptions<EstimatesServiceOptions>>().Value;
            var attemptTimeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);

            pipeline.AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
            if (options.MaxRetryAttempts > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                });
            }

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = attemptTimeout * 10,
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(15),
            });
            pipeline.AddTimeout(attemptTimeout);
        });

        return builder;
    }
}

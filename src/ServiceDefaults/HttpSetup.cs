using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceDefaults;

public static class HttpSetup
{
    public static IServiceCollection AddApiCors(this IServiceCollection services, CorsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return services.AddCors(options =>
        {
            // Explicit allow-list. Bearer tokens are sent in a header, not cookies, so credentials are not allowed.
            options.AddPolicy(CorsSettings.PolicyName, policy =>
            {
                policy.WithOrigins(settings.AllowedOrigins)
                    .WithMethods("POST")
                    .WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.HeaderName, "GraphQL-Preflight")
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });
    }

    public static IServiceCollection AddPerClientRateLimiting(this IServiceCollection services, RateLimitingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // One bucket per client application ("azp"), falling back to the user ("sub") and then the IP.
            // This runs after authentication, so the claims come from a validated token.
            options.AddPolicy(RateLimitingSettings.PolicyName, httpContext =>
            {
                string partitionKey = httpContext.User.FindFirstValue("azp")
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                });
            });
        });
    }

    /// <summary>
    /// Liveness: is the process running? No dependencies, so a database outage does not cause restarts.
    /// Readiness: can the service do useful work? Checks tagged "ready" (for example the database).
    /// Load balancers and Kubernetes stop sending traffic while readiness fails.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        return endpoints;
    }
}

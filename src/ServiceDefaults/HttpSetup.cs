using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

    public const string HealthPathPrefix = "/health";

    /// <summary>
    /// Caps how long readiness checks may run. Without a cap, an unreachable database makes
    /// /health/ready take ~20 s (EF Core keeps retrying the connection), longer than a load
    /// balancer's health-check timeout, so slow checks pile up. A capped check reports 503 quickly.
    /// Call after the checks are registered.
    /// </summary>
    public static IServiceCollection AddReadinessCheckTimeout(this IServiceCollection services, TimeSpan timeout)
    {
        return services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (HealthCheckRegistration registration in options.Registrations.Where(check => check.Tags.Contains("ready")))
            {
                registration.Timeout = timeout;
            }
        });
    }

    /// <summary>
    /// HSTS plus HTTP-to-HTTPS redirection for client traffic, with health endpoints exempt.
    ///
    /// Behind a TLS-terminating load balancer (for example an AWS ALB), health checks reach the
    /// container over plain HTTP inside the network and never carry X-Forwarded-Proto. Redirecting
    /// them would make every target look unhealthy. Health responses contain no data and need no
    /// token, so serving them over HTTP is safe; every other path still redirects to HTTPS.
    ///
    /// The redirect only happens when the public HTTPS port is known (ASPNETCORE_HTTPS_PORT or the
    /// "https_port" setting). Without it ASP.NET Core logs a warning and does NOT redirect.
    /// </summary>
    public static IApplicationBuilder UseHttpsEnforcement(this IApplicationBuilder app)
    {
        app.UseHsts();
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments(HealthPathPrefix),
            branch => branch.UseHttpsRedirection());
        return app;
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

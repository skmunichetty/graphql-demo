using ServiceDefaults.Security;

namespace Estimates.Api.Security;

/// <summary>OAuth scopes this API understands. Scopes describe what a client may do.</summary>
public static class Scopes
{
    public const string EstimatesRead = "estimates:read";
    public const string EstimatesWrite = "estimates:write";

    /// <summary>
    /// Granted only to trusted service identities (the Accounting service). It allows reading an
    /// estimate for a tenant named in the request, because a service token is not tied to one tenant.
    /// </summary>
    public const string EstimatesIntegrationRead = "estimates:integration.read";
}

/// <summary>ASP.NET Core authorization policy names used by [Authorize(Policy = ...)] in the schema.</summary>
public static class Policies
{
    public const string EstimatesRead = "estimates.read";
    public const string EstimatesWrite = "estimates.write";
    public const string EstimatesIntegrationRead = "estimates.integration.read";
}

public static class EstimatesAuthorization
{
    public static IServiceCollection AddEstimatesAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        string tenantClaimType = configuration[$"{JwtAuthenticationOptions.SectionName}:TenantClaimType"] ?? "tenant_id";

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.EstimatesRead, policy => policy.RequireTenantUserWithScope(tenantClaimType, Scopes.EstimatesRead))
            .AddPolicy(Policies.EstimatesWrite, policy => policy.RequireTenantUserWithScope(tenantClaimType, Scopes.EstimatesWrite))

            // Service identities have no tenant claim; they may only read, and only with this scope.
            .AddPolicy(Policies.EstimatesIntegrationRead, policy => policy.RequireScope(Scopes.EstimatesIntegrationRead));

        return services;
    }
}

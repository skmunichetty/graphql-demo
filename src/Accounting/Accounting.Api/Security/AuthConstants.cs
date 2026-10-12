using ServiceDefaults.Security;

namespace Accounting.Api.Security;

/// <summary>OAuth scopes this API understands.</summary>
public static class Scopes
{
    public const string AccountingExport = "accounting:export";
    public const string AccountingRead = "accounting:read";
}

/// <summary>Authorization policy names used by [Authorize(Policy = ...)] in the schema.</summary>
public static class Policies
{
    public const string AccountingExport = "accounting.export";
    public const string AccountingRead = "accounting.read";
}

public static class AccountingAuthorization
{
    public static IServiceCollection AddAccountingAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        string tenantClaimType = configuration[$"{JwtAuthenticationOptions.SectionName}:TenantClaimType"] ?? "tenant_id";

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AccountingExport, policy => policy.RequireTenantUserWithScope(tenantClaimType, Scopes.AccountingExport))
            .AddPolicy(Policies.AccountingRead, policy => policy.RequireTenantUserWithScope(tenantClaimType, Scopes.AccountingRead));

        return services;
    }
}

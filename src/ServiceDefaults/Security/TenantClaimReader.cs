using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace ServiceDefaults.Security;

/// <summary>
/// Reads the caller's tenant from the validated access token. Resolvers use this instead of
/// accepting a tenant argument, so a client can never choose which tenant it acts for.
/// Registered as a singleton: it only holds configuration.
/// </summary>
public sealed class TenantClaimReader
{
    private readonly string _tenantClaimType;

    public TenantClaimReader(IOptions<JwtAuthenticationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tenantClaimType = options.Value.TenantClaimType;
    }

    public string GetRequiredTenantId(ClaimsPrincipal user)
    {
        return user.GetRequiredTenantId(_tenantClaimType);
    }
}

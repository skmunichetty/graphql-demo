using System.Security.Claims;

namespace ServiceDefaults.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns true when the token grants the scope. Identity providers differ in how they send
    /// scopes: Keycloak and Auth0 use a space-separated "scope" claim, Microsoft Entra ID uses "scp".
    /// Auth0 can also put API permissions in a "permissions" array. All three are checked.
    /// </summary>
    public static bool HasScope(this ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (Claim claim in user.Claims)
        {
            bool isScopeClaim = claim.Type == "scope" || claim.Type == "scp" || claim.Type == "permissions";
            if (!isScopeClaim)
            {
                continue;
            }

            string[] values = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Contains(scope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the tenant from the validated token. The tenant is never read from request input.
    /// Authorization policies already require the claim, so a missing claim here is a server bug
    /// and is reported as an internal error rather than a client error.
    /// </summary>
    public static string GetRequiredTenantId(this ClaimsPrincipal user, string tenantClaimType)
    {
        ArgumentNullException.ThrowIfNull(user);

        string? tenantId = user.FindFirst(tenantClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                $"The authenticated principal has no '{tenantClaimType}' claim. Check the authorization policy.");
        }

        return tenantId;
    }
}

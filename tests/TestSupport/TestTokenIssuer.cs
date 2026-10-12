using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TestSupport;

/// <summary>
/// TEST-ONLY token signer. Integration tests cannot depend on a running identity provider, so
/// they sign tokens with an in-memory RSA key and configure the API to trust that key.
/// The API still performs full validation (signature, issuer, audience, lifetime, algorithm).
/// This class must never be referenced by production code.
/// </summary>
public sealed class TestTokenIssuer
{
    public const string Issuer = "https://identity.test.invalid/realms/tests";

    public TestTokenIssuer()
    {
        SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-signing-key" };
    }

    public RsaSecurityKey SigningKey { get; }

    /// <summary>Creates a user token for a tenant with the given scopes.</summary>
    public string CreateUserToken(string audience, string tenantId, params string[] scopes)
    {
        var claims = new Dictionary<string, object>
        {
            ["tenant_id"] = tenantId,
            ["azp"] = "test-client",
        };
        return CreateToken(audience, subject: $"user-{tenantId}", scopes, claims);
    }

    /// <summary>Creates a client-credentials style service token: no tenant claim.</summary>
    public string CreateServiceToken(string audience, string clientId, params string[] scopes)
    {
        var claims = new Dictionary<string, object> { ["azp"] = clientId };
        return CreateToken(audience, subject: $"service-account-{clientId}", scopes, claims);
    }

    public string CreateToken(
        string audience,
        string subject,
        IEnumerable<string> scopes,
        IDictionary<string, object> extraClaims,
        DateTime? expires = null,
        string issuer = Issuer,
        SecurityKey? signingKey = null)
    {
        ArgumentNullException.ThrowIfNull(extraClaims);

        DateTime expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        var claims = new Dictionary<string, object>(extraClaims)
        {
            ["scope"] = string.Join(' ', scopes),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            Claims = claims,
            IssuedAt = expiresAt.AddMinutes(-10),
            NotBefore = expiresAt.AddMinutes(-10),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

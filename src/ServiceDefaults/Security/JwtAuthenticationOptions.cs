using System.ComponentModel.DataAnnotations;

namespace ServiceDefaults.Security;

/// <summary>
/// Settings for validating access tokens from the OpenID Connect identity provider
/// (section "Authentication"). Validated at startup: the service refuses to start without them.
/// </summary>
public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// The token issuer, for example "http://localhost:8180/realms/construction" (Keycloak)
    /// or "https://your-tenant.auth0.com/" (Auth0). Must match the token "iss" claim exactly.
    /// </summary>
    [Required]
    [Url]
    public string Authority { get; set; } = string.Empty;

    /// <summary>The API identifier that must appear in the token "aud" claim, for example "estimates-api".</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Optional discovery document URL when the service reaches the identity provider on a different
    /// address than the public issuer (for example "http://keycloak:8080/..." inside Docker).
    /// </summary>
    [Url]
    public string? MetadataAddress { get; set; }

    /// <summary>Must be true outside local development so signing keys are only fetched over HTTPS.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>The claim that carries the tenant identifier. Configured as a user attribute in the identity provider.</summary>
    [Required]
    public string TenantClaimType { get; set; } = "tenant_id";
}

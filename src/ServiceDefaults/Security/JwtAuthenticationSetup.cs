using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ServiceDefaults.Security;

public static class JwtAuthenticationSetup
{
    /// <summary>
    /// Validates JWT access tokens issued by an external OpenID Connect provider (Keycloak locally,
    /// Auth0 or another provider in production). This service never issues tokens or stores passwords.
    /// Signing keys are downloaded from the provider's discovery document and refreshed automatically.
    /// </summary>
    public static IServiceCollection AddJwtBearerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TenantClaimReader>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure JwtBearer from the validated options (rather than raw configuration) so that
        // tests can override the same options object.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>>((bearer, jwtOptionsAccessor) =>
            {
                JwtAuthenticationOptions jwtOptions = jwtOptionsAccessor.Value;

                bearer.Authority = jwtOptions.Authority;
                bearer.Audience = jwtOptions.Audience;
                bearer.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
                if (!string.IsNullOrWhiteSpace(jwtOptions.MetadataAddress))
                {
                    bearer.MetadataAddress = jwtOptions.MetadataAddress;
                }

                // Keep claim names exactly as issued ("sub", "scope", "tenant_id") instead of mapping
                // them to long legacy XML claim type URIs.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,

                    // Must match the token "iss" exactly (Auth0 issuers end with "/", Keycloak's do not).
                    ValidIssuer = jwtOptions.Authority,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,

                    // Only asymmetric signatures: the API holds no secret that could forge tokens.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                    // Small allowance for clock differences between the identity provider and this server.
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Log the reason (expired, bad signature, wrong audience...) but never the token itself.
                        ILogger logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("ServiceDefaults.Authentication");
                        logger.LogWarning("Access token rejected: {FailureType}", context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>A tenant user (token has a tenant claim) whose token grants the scope.</summary>
    public static AuthorizationPolicyBuilder RequireTenantUserWithScope(
        this AuthorizationPolicyBuilder policy,
        string tenantClaimType,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.RequireAuthenticatedUser();

        // A token without a tenant (for example a service token) cannot use tenant-scoped operations.
        policy.RequireClaim(tenantClaimType);
        policy.RequireAssertion(context => context.User.HasScope(scope));
        return policy;
    }

    /// <summary>Any authenticated caller (user or service) whose token grants the scope.</summary>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder policy, string scope)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => context.User.HasScope(scope));
        return policy;
    }
}

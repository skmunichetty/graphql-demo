using System.Security.Claims;
using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ServiceDefaults.GraphQL;

/// <summary>
/// Authenticates GraphQL WebSocket connections. Browsers cannot send an Authorization header when
/// opening a WebSocket, so clients send the access token in the connection_init payload instead:
///   { "type": "connection_init", "payload": { "Authorization": "Bearer &lt;token&gt;" } }
/// An Authorization header on the upgrade request is accepted too (non-browser clients).
/// The token is validated with exactly the same JwtBearer settings as HTTP requests; a connection
/// without a valid token is rejected before any operation runs.
/// Because the token travels in a message rather than a cookie, a malicious site cannot reuse a
/// victim's session (cross-site WebSocket hijacking).
/// </summary>
public sealed class WebSocketAuthenticationInterceptor : DefaultSocketSessionInterceptor
{
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(connectionInitMessage);

        HttpContext httpContext = session.Connection.HttpContext;
        ILogger logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger<WebSocketAuthenticationInterceptor>();

        string? token = ReadBearerToken(connectionInitMessage.Payload) ?? ReadBearerToken(httpContext.Request.Headers.Authorization);
        if (token is null)
        {
            logger.LogWarning("WebSocket connection rejected: no access token in connection_init");
            return ConnectionStatus.Reject("Unauthorized: send \"Authorization\": \"Bearer <token>\" in the connection_init payload.");
        }

        ClaimsPrincipal? principal = await ValidateAsync(httpContext, token, logger, cancellationToken);
        if (principal is null)
        {
            return ConnectionStatus.Reject("Unauthorized: the access token is invalid or expired.");
        }

        // Resolvers and [Authorize] policies read the user from the HttpContext, as for HTTP requests.
        httpContext.User = principal;
        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    private static async Task<ClaimsPrincipal?> ValidateAsync(HttpContext httpContext, string token, ILogger logger, CancellationToken cancellationToken)
    {
        JwtBearerOptions options = httpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
        if (options.ConfigurationManager is not null)
        {
            // Signing keys from the identity provider's discovery document (cached and refreshed).
            OpenIdConnectConfiguration configuration = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            parameters.IssuerSigningKeys = configuration.SigningKeys;
        }

        foreach (TokenHandler handler in options.TokenHandlers)
        {
            TokenValidationResult result = await handler.ValidateTokenAsync(token, parameters);
            if (result.IsValid)
            {
                return new ClaimsPrincipal(result.ClaimsIdentity);
            }

            // Log the reason (expired, bad signature, wrong audience...) but never the token itself.
            logger.LogWarning("WebSocket access token rejected: {FailureType}", result.Exception?.GetType().Name);
        }

        return null;
    }

    private static string? ReadBearerToken(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return ReadBearerToken(property.Value.GetString());
            }
        }

        return null;
    }

    private static string? ReadBearerToken(string? authorization)
    {
        const string prefix = "Bearer ";
        return authorization is not null && authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }
}

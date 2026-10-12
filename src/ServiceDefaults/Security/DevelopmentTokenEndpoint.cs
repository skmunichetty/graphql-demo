using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.Security;

/// <summary>
/// Local token helper (section "DevelopmentToken"). Off by default; only allowed in the Development
/// environment. It forwards a password grant to the demo identity provider so a developer can get an
/// access token with one HTTP call. The API still never issues or signs tokens itself.
/// </summary>
public sealed class DevelopmentTokenSettings
{
    public const string SectionName = "DevelopmentToken";

    public bool Enabled { get; set; }

    /// <summary>The identity provider's token endpoint, for example Keycloak's ".../protocol/openid-connect/token".</summary>
    [Url]
    public string? TokenEndpoint { get; set; }

    /// <summary>The demo-only public client that allows the password grant.</summary>
    [Required]
    public string ClientId { get; set; } = "construction-dev-cli";

    /// <summary>Used when the request has no password (the shared demo user password). Keep in user secrets or .env.</summary>
    public string? DefaultPassword { get; set; }

    [Required]
    public string DefaultScopes { get; set; } = "estimates:read estimates:write accounting:export accounting:read";
}

/// <summary>Request body for POST /dev/token. Password and scope are optional.</summary>
public sealed record DevelopmentTokenRequest(string Username, string? Password, string? Scope);

/// <summary>Response body for POST /dev/token.</summary>
public sealed record DevelopmentTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string? Scope);

public static class DevelopmentTokenEndpoint
{
    public const string Route = "/dev/token";
    private const string HttpClientName = "development-token";

    public static IServiceCollection AddDevelopmentTokenEndpoint(
        this IServiceCollection services,
        DevelopmentTokenSettings settings,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);

        if (!settings.Enabled)
        {
            return services;
        }

        // Fail fast: a password-grant helper must never be reachable outside a developer machine.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{DevelopmentTokenSettings.SectionName}:Enabled is only allowed in the Development environment (current: {environment.EnvironmentName}).");
        }

        if (string.IsNullOrWhiteSpace(settings.TokenEndpoint))
        {
            throw new InvalidOperationException($"{DevelopmentTokenSettings.SectionName}:TokenEndpoint is required when the endpoint is enabled.");
        }

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }

    public static IEndpointRouteBuilder MapDevelopmentTokenEndpoint(this IEndpointRouteBuilder endpoints, DevelopmentTokenSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return endpoints;
        }

        endpoints.MapPost(Route, (DevelopmentTokenRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
                RequestTokenAsync(request, settings, httpClientFactory, loggerFactory.CreateLogger(typeof(DevelopmentTokenEndpoint)), cancellationToken))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSettings.PolicyName);

        return endpoints;
    }

    private static async Task<IResult> RequestTokenAsync(
        DevelopmentTokenRequest request,
        DevelopmentTokenSettings settings,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["The username is required."] });
        }

        string? password = string.IsNullOrEmpty(request.Password) ? settings.DefaultPassword : request.Password;
        if (string.IsNullOrEmpty(password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = [$"The password is required: no {DevelopmentTokenSettings.SectionName}:DefaultPassword is configured."],
            });
        }

        string scope = string.IsNullOrWhiteSpace(request.Scope) ? settings.DefaultScopes : request.Scope;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = settings.ClientId,
            ["username"] = request.Username,
            ["password"] = password,
            ["scope"] = $"openid {scope}",
        };

        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await httpClient.PostAsync(new Uri(settings.TokenEndpoint!), content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorResponse? error = await TryReadErrorAsync(response, cancellationToken);
                logger.LogWarning(
                    "Development token request for {Username} failed with HTTP {StatusCode} ({Error})",
                    request.Username,
                    (int)response.StatusCode,
                    error?.Error);

                // invalid_grant (wrong user or password) and invalid_scope are the caller's mistake.
                int statusCode = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    ? (int)response.StatusCode
                    : StatusCodes.Status502BadGateway;
                return Results.Problem(
                    title: "The identity provider rejected the token request.",
                    detail: error?.Description ?? error?.Error,
                    statusCode: statusCode);
            }

            TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                return Results.Problem(title: "The identity provider returned an empty token response.", statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new DevelopmentTokenResponse(token.AccessToken, "Bearer", token.ExpiresIn, token.Scope));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The identity provider at {TokenEndpoint} could not be reached", settings.TokenEndpoint);
            return Results.Problem(title: "The identity provider could not be reached.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<ErrorResponse?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? Description { get; set; }
    }
}

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Accounting.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.EstimatesService;

/// <summary>
/// Obtains access tokens for this service's own identity using the OAuth 2.0 client credentials
/// grant, and caches each token until shortly before it expires. This is a token CLIENT: the
/// identity provider issues and signs the tokens.
/// Registered as a singleton so the cache is shared by all requests.
/// </summary>
public sealed class ClientCredentialsTokenProvider : IDisposable
{
    public const string HttpClientName = "identity-provider";

    // Refresh a little early so a token never expires while a request is in flight.
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EstimatesServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientCredentialsTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _refreshAt;

    public ClientCredentialsTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<EstimatesServiceOptions> options,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _timeProvider.GetUtcNow() < _refreshAt)
        {
            return _accessToken;
        }

        // Only one caller refreshes; the others wait and then reuse the new token.
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _timeProvider.GetUtcNow() < _refreshAt)
            {
                return _accessToken;
            }

            TokenResponse token = await RequestTokenAsync(cancellationToken);
            _accessToken = token.AccessToken;
            _refreshAt = _timeProvider.GetUtcNow().AddSeconds(token.ExpiresInSeconds) - RefreshBeforeExpiry;
            return _accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["scope"] = _options.Scope,
        };

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await httpClient.PostAsync(new Uri(_options.TokenEndpoint), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The response body may contain details that help an attacker; log the status only.
                _logger.LogError("Token request for client {ClientId} failed with HTTP {StatusCode}", _options.ClientId, (int)response.StatusCode);
                throw new EstimatesServiceUnavailableException("Could not obtain a service access token.");
            }

            TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new EstimatesServiceUnavailableException("The identity provider returned an empty token response.");
            }

            return token;
        }
        catch (HttpRequestException exception)
        {
            throw new EstimatesServiceUnavailableException("The identity provider could not be reached.", exception);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; set; }
    }
}

/// <summary>Adds this service's access token to outgoing requests to the Estimates service.</summary>
public sealed class ServiceAuthenticationHandler : DelegatingHandler
{
    private readonly ClientCredentialsTokenProvider _tokenProvider;

    public ServiceAuthenticationHandler(ClientCredentialsTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

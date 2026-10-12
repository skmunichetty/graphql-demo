using System.Net;
using System.Text;
using Accounting.Application;
using Accounting.Domain;
using Accounting.Infrastructure.EstimatesService;
using Accounting.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Accounting.UnitTests;

public sealed class MockAccountingProviderTests
{
    private readonly MockAccountingProvider _provider = new(
        Options.Create(new MockAccountingProviderOptions { SimulatedLatencyMilliseconds = 0 }),
        NullLogger<MockAccountingProvider>.Instance);

    private static AccountingInvoice Invoice(string customerName)
    {
        return AccountingInvoice.FromEstimate("tenant-a", TestData.Estimate(Guid.NewGuid(), customerName: customerName));
    }

    [Fact]
    public async Task Same_idempotency_key_returns_same_reference()
    {
        AccountingInvoice invoice = Invoice("Acme");

        string first = await _provider.ExportInvoiceAsync(invoice, "key-1", TestContext.Current.CancellationToken);
        string second = await _provider.ExportInvoiceAsync(invoice, "key-1", TestContext.Current.CancellationToken);
        string other = await _provider.ExportInvoiceAsync(invoice, "key-2", TestContext.Current.CancellationToken);

        Assert.StartsWith("MOCK-INV-", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task Customer_name_containing_trigger_simulates_a_rejection()
    {
        var exception = await Assert.ThrowsAsync<AccountingProviderException>(() =>
            _provider.ExportInvoiceAsync(Invoice("Acme MOCK-FAIL Pty"), "key-1", TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.ProviderRejected, exception.ErrorCode);
    }
}

public sealed class EstimatesServiceClientTests
{
    private static EstimatesServiceClient CreateClient(HttpStatusCode statusCode, string body)
    {
        var httpClient = new HttpClient(new StubHandler(statusCode, body)) { BaseAddress = new Uri("http://estimates.test") };
        return new EstimatesServiceClient(httpClient, NullLogger<EstimatesServiceClient>.Instance);
    }

    [Fact]
    public async Task Parses_estimate_from_graphql_response()
    {
        Guid estimateId = Guid.NewGuid();
        string body = """
            {"data":{"estimateForIntegration":{"id":"ESTIMATE_ID","status":"APPROVED","currencyCode":"AUD",
             "subtotal":100.00,"taxTotal":10.00,"total":110.00,"customer":{"name":"Acme","email":null},
             "lineItems":[{"description":"Work","quantity":1,"unitPrice":100,"taxRatePercent":10,"subtotal":100.00,"taxAmount":10.00,"total":110.00}]}}}
            """.Replace("ESTIMATE_ID", estimateId.ToString(), StringComparison.Ordinal);

        EstimateSnapshot? estimate = await CreateClient(HttpStatusCode.OK, body)
            .GetEstimateAsync("tenant-a", estimateId, TestContext.Current.CancellationToken);

        Assert.NotNull(estimate);
        Assert.True(estimate.IsApproved);
        Assert.Equal(110.00m, estimate.Total);
        Assert.Equal("Work", Assert.Single(estimate.Lines).Description);
    }

    [Fact]
    public async Task Returns_null_when_estimate_does_not_exist()
    {
        EstimateSnapshot? estimate = await CreateClient(HttpStatusCode.OK, """{"data":{"estimateForIntegration":null}}""")
            .GetEstimateAsync("tenant-a", Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(estimate);
    }

    [Fact]
    public async Task GraphQL_errors_with_http_200_are_treated_as_unavailable()
    {
        string body = """{"errors":[{"message":"Not authorized","extensions":{"code":"AUTH_NOT_AUTHORIZED"}}],"data":{"estimateForIntegration":null}}""";

        var exception = await Assert.ThrowsAsync<EstimatesServiceUnavailableException>(() => CreateClient(HttpStatusCode.OK, body)
            .GetEstimateAsync("tenant-a", Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Contains("AUTH_NOT_AUTHORIZED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_failure_is_treated_as_unavailable()
    {
        await Assert.ThrowsAsync<EstimatesServiceUnavailableException>(() => CreateClient(HttpStatusCode.BadGateway, "")
            .GetEstimateAsync("tenant-a", Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Embedded_operation_is_the_contract_file()
    {
        // The client's operation comes from the same file the Estimates contract test executes.
        string? resource = typeof(EstimatesServiceClient).Assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("EstimateForExport.graphql", StringComparison.Ordinal));

        Assert.NotNull(resource);
    }
}

public sealed class ClientCredentialsTokenProviderTests
{
    [Fact]
    public async Task Token_is_cached_until_shortly_before_expiry()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"access_token":"token-1","expires_in":300,"token_type":"Bearer"}""");
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tokenProvider = new ClientCredentialsTokenProvider(
            new StubHttpClientFactory(handler),
            Options.Create(new EstimatesServiceOptions
            {
                TokenEndpoint = "http://identity.test/token",
                ClientId = "accounting-service",
                ClientSecret = "not-a-real-secret",
                Scope = "estimates:integration.read",
            }),
            time,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        await tokenProvider.GetAccessTokenAsync(TestContext.Current.CancellationToken);
        await tokenProvider.GetAccessTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.RequestCount);

        // 300 s lifetime, refreshed 60 s early: after 241 s a new token is requested.
        time.Advance(TimeSpan.FromSeconds(241));
        await tokenProvider.GetAccessTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains("grant_type=client_credentials", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_token_request_is_reported_as_unavailable()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        using var tokenProvider = new ClientCredentialsTokenProvider(
            new StubHttpClientFactory(handler),
            Options.Create(new EstimatesServiceOptions { TokenEndpoint = "http://identity.test/token", ClientId = "x", ClientSecret = "y" }),
            TimeProvider.System,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        await Assert.ThrowsAsync<EstimatesServiceUnavailableException>(() =>
            tokenProvider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public StubHandler(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _body = body;
    }

    public int RequestCount { get; private set; }

    public string LastRequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public StubHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }
}

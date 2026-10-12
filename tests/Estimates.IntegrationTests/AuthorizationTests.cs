using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using TestSupport;
using static Estimates.IntegrationTests.EstimateTestHelpers;

namespace Estimates.IntegrationTests;

/// <summary>
/// Authentication (who are you?) is checked at the HTTP level and fails with 401.
/// Authorization (may you do this?) is checked per field and fails with a GraphQL error.
/// Tenant isolation is checked in the data access layer and looks like "not found".
/// </summary>
public sealed class AuthorizationTests
{
    private readonly EstimatesApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public AuthorizationTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    private static readonly object ListVariables = new { first = 5 };

    [Fact]
    public async Task Request_without_token_is_rejected_with_401()
    {
        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, accessToken: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected_with_401()
    {
        string token = _factory.Tokens.CreateUserToken("accounting-api", NewTenantId(), "estimates:read");

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected_with_401()
    {
        string token = _factory.Tokens.CreateToken(
            EstimatesApiFactory.Audience,
            "user-1",
            ["estimates:read"],
            new Dictionary<string, object> { ["tenant_id"] = NewTenantId() },
            expires: DateTime.UtcNow.AddMinutes(-5));

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_by_an_unknown_key_is_rejected_with_401()
    {
        using var otherRsa = System.Security.Cryptography.RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "attacker-key" };
        string token = _factory.Tokens.CreateToken(
            EstimatesApiFactory.Audience,
            "user-1",
            ["estimates:read"],
            new Dictionary<string, object> { ["tenant_id"] = NewTenantId() },
            signingKey: otherKey);

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_from_another_issuer_is_rejected_with_401()
    {
        string token = _factory.Tokens.CreateToken(
            EstimatesApiFactory.Audience,
            "user-1",
            ["estimates:read"],
            new Dictionary<string, object> { ["tenant_id"] = NewTenantId() },
            issuer: "https://evil.example/realms/tests");

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_token_cannot_create_estimates()
    {
        string token = _factory.CreateUserToken(NewTenantId(), "estimates:read");
        var variables = new { input = new { customerName = "Acme", currencyCode = "AUD" } };

        GraphQLResponse response = await _client.SendAsync(Operations.CreateEstimate, variables, token, TestContext.Current.CancellationToken);

        // Authenticated, so HTTP succeeds; the field is refused with a GraphQL authorization error.
        Assert.Equal("AUTH_NOT_AUTHORIZED", response.FirstErrorCode);
        Assert.Null(response.Data?["createEstimate"]);
    }

    [Fact]
    public async Task Write_only_token_cannot_read_estimates()
    {
        string token = _factory.CreateUserToken(NewTenantId(), "estimates:write");

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal("AUTH_NOT_AUTHORIZED", response.FirstErrorCode);
    }

    [Fact]
    public async Task Token_without_tenant_claim_cannot_use_tenant_operations()
    {
        string token = _factory.Tokens.CreateServiceToken(EstimatesApiFactory.Audience, "some-service", "estimates:read", "estimates:write");

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, ListVariables, token, TestContext.Current.CancellationToken);

        Assert.Equal("AUTH_NOT_AUTHORIZED", response.FirstErrorCode);
    }

    [Fact]
    public async Task Other_tenant_cannot_read_list_or_change_an_estimate()
    {
        string ownerToken = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        string otherTenantToken = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode estimate = await CreateEstimateAsync(_client, ownerToken, customerName: "Owner's customer");
        string estimateId = estimate["id"]!.GetValue<string>();

        GraphQLResponse read = await _client.SendAsync(Operations.GetEstimate, new { id = estimateId }, otherTenantToken, TestContext.Current.CancellationToken);
        Assert.Null(read.Errors);
        Assert.Null(read.Data!["estimate"]);

        GraphQLResponse list = await _client.SendAsync(Operations.ListEstimates, ListVariables, otherTenantToken, TestContext.Current.CancellationToken);
        Assert.Empty(list.Data!["estimates"]!["nodes"]!.AsArray());

        var approveVariables = new { input = new { estimateId, expectedVersion = 1 } };
        GraphQLResponse approve = await _client.SendAsync(Operations.ApproveEstimate, approveVariables, otherTenantToken, TestContext.Current.CancellationToken);
        JsonNode error = RequireBusinessError(approve, "approveEstimate");
        Assert.Equal("ESTIMATE_NOT_FOUND", error["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Service_identity_with_integration_scope_can_read_estimate_for_named_tenant()
    {
        string tenantId = NewTenantId();
        string userToken = _factory.CreateUserToken(tenantId, ReadWriteScopes);
        JsonNode estimate = await CreateEstimateAsync(_client, userToken);
        string serviceToken = _factory.Tokens.CreateServiceToken(EstimatesApiFactory.Audience, "accounting-service", "estimates:integration.read");

        var variables = new { tenantId, id = estimate["id"]!.GetValue<string>() };
        GraphQLResponse response = await _client.SendAsync(Operations.GetEstimateForIntegration, variables, serviceToken, TestContext.Current.CancellationToken);

        Assert.Null(response.Errors);
        Assert.Equal("DRAFT", response.Data!["estimateForIntegration"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Service_identity_cannot_write_or_use_tenant_queries()
    {
        string serviceToken = _factory.Tokens.CreateServiceToken(EstimatesApiFactory.Audience, "accounting-service", "estimates:integration.read");
        var variables = new { input = new { customerName = "Acme", currencyCode = "AUD" } };

        GraphQLResponse create = await _client.SendAsync(Operations.CreateEstimate, variables, serviceToken, TestContext.Current.CancellationToken);
        GraphQLResponse list = await _client.SendAsync(Operations.ListEstimates, ListVariables, serviceToken, TestContext.Current.CancellationToken);

        Assert.Equal("AUTH_NOT_AUTHORIZED", create.FirstErrorCode);
        Assert.Equal("AUTH_NOT_AUTHORIZED", list.FirstErrorCode);
    }

    [Fact]
    public async Task User_token_cannot_use_integration_query_to_pick_a_tenant()
    {
        string userToken = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        var variables = new { tenantId = "some-other-tenant", id = Guid.NewGuid() };

        GraphQLResponse response = await _client.SendAsync(Operations.GetEstimateForIntegration, variables, userToken, TestContext.Current.CancellationToken);

        Assert.Equal("AUTH_NOT_AUTHORIZED", response.FirstErrorCode);
    }
}

using System.Text.Json.Nodes;
using TestSupport;
using static Estimates.IntegrationTests.EstimateTestHelpers;
using Path = System.IO.Path;

namespace Estimates.IntegrationTests;

/// <summary>
/// Consumer-driven contract test. It executes the exact GraphQL document that the Accounting
/// service embeds in its client (src/Accounting/.../EstimateForExport.graphql), with a service
/// token, against this service. If a schema change here would break the Accounting service,
/// this test fails in the Estimates build, before anything is deployed.
/// </summary>
public sealed class ConsumerContractTests
{
    private readonly EstimatesApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public ConsumerContractTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    [Fact]
    public async Task Accounting_service_export_query_is_supported()
    {
        string tenantId = NewTenantId();
        string userToken = _factory.CreateUserToken(tenantId, ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, userToken);
        JsonNode withItem = await AddLineItemAsync(_client, userToken, draft, "Roof sheeting", 12m, 89.95m, 10m);
        JsonNode approved = await ApproveAsync(_client, userToken, withItem);

        string contractPath = Path.Combine(
            FindRepositoryRoot(), "src", "Accounting", "Accounting.Infrastructure", "EstimatesService", "EstimateForExport.graphql");
        string accountingQuery = await File.ReadAllTextAsync(contractPath, TestContext.Current.CancellationToken);
        string serviceToken = _factory.Tokens.CreateServiceToken(EstimatesApiFactory.Audience, "accounting-service", "estimates:integration.read");

        var variables = new { tenantId, id = approved["id"]!.GetValue<string>() };
        GraphQLResponse response = await _client.SendAsync(accountingQuery, variables, serviceToken, TestContext.Current.CancellationToken);

        Assert.Null(response.Errors);
        JsonNode estimate = response.Data!["estimateForIntegration"]!;
        Assert.Equal("APPROVED", estimate["status"]!.GetValue<string>());
        Assert.Equal(1187.34m, estimate["total"]!.GetValue<decimal>());
        Assert.Equal("Roof sheeting", estimate["lineItems"]![0]!["description"]!.GetValue<string>());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConstructionEstimation.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not find the repository root (ConstructionEstimation.slnx).");
        }

        return directory.FullName;
    }
}

using System.Text.Json.Nodes;
using TestSupport;
using static Estimates.IntegrationTests.EstimateTestHelpers;

namespace Estimates.IntegrationTests;

/// <summary>Pagination, request limits and environment-specific GraphQL behaviour.</summary>
public sealed class QueryBehaviourTests
{
    private readonly EstimatesApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public QueryBehaviourTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    [Fact]
    public async Task Estimates_are_cursor_paginated_newest_first()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode first = await CreateEstimateAsync(_client, token, customerName: "Customer 1");
        await AddLineItemAsync(_client, token, first, "Line for customer 1", 1m, 10m, 0m);
        await CreateEstimateAsync(_client, token, customerName: "Customer 2");
        await CreateEstimateAsync(_client, token, customerName: "Customer 3");

        GraphQLResponse pageOne = await _client.SendAsync(Operations.ListEstimates, new { first = 2 }, token, TestContext.Current.CancellationToken);
        Assert.Null(pageOne.Errors);
        JsonNode connection = pageOne.Data!["estimates"]!;
        string[] pageOneNames = connection["nodes"]!.AsArray().Select(node => node!["customer"]!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(["Customer 3", "Customer 2"], pageOneNames);
        Assert.True(connection["pageInfo"]!["hasNextPage"]!.GetValue<bool>());

        string endCursor = connection["pageInfo"]!["endCursor"]!.GetValue<string>();
        GraphQLResponse pageTwo = await _client.SendAsync(Operations.ListEstimates, new { first = 2, after = endCursor }, token, TestContext.Current.CancellationToken);
        Assert.Null(pageTwo.Errors);
        JsonArray pageTwoNodes = pageTwo.Data!["estimates"]!["nodes"]!.AsArray();
        JsonNode onlyNode = Assert.Single(pageTwoNodes)!;
        Assert.Equal("Customer 1", onlyNode["customer"]!["name"]!.GetValue<string>());
        Assert.Equal("Line for customer 1", onlyNode["lineItems"]![0]!["description"]!.GetValue<string>());
        Assert.False(pageTwo.Data!["estimates"]!["pageInfo"]!["hasNextPage"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Line_items_for_a_page_of_estimates_are_loaded_in_one_query()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ReadWriteScopes);
        for (int index = 1; index <= 4; index++)
        {
            JsonNode estimate = await CreateEstimateAsync(_client, token, customerName: $"Customer {index}");
            await AddLineItemAsync(_client, token, estimate, $"Line {index}", 1m, 10m, 0m);
        }

        int lineItemQueriesBefore = _factory.SqlCommands.CountFor("line_items", tenantId);
        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, new { first = 10 }, token, TestContext.Current.CancellationToken);
        int lineItemQueries = _factory.SqlCommands.CountFor("line_items", tenantId) - lineItemQueriesBefore;

        Assert.Null(response.Errors);
        Assert.Equal(4, response.Data!["estimates"]!["nodes"]!.AsArray().Count);

        // Without the DataLoader this would be 4 queries (one per estimate): the N+1 problem.
        Assert.Equal(1, lineItemQueries);
    }

    [Fact]
    public async Task Page_size_above_the_maximum_is_rejected()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);

        GraphQLResponse response = await _client.SendAsync(Operations.ListEstimates, new { first = 51 }, token, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Errors);
        Assert.Null(response.Data?["estimates"]);
    }

    [Fact]
    public async Task Introspection_is_disabled_outside_development()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);

        GraphQLResponse response = await _client.SendAsync("{ __schema { types { name } } }", null, token, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Errors);
        Assert.Null(response.Data?["__schema"]);
    }

    [Fact]
    public async Task Syntax_errors_are_reported_as_graphql_errors_without_data()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);

        GraphQLResponse response = await _client.SendAsync("{ estimates { nodes { id }", null, token, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Errors);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task Unknown_fields_are_rejected_by_validation()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);

        GraphQLResponse response = await _client.SendAsync("{ estimates { nodes { tenantId } } }", null, token, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Errors);
    }
}

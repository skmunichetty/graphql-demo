using System.Text.Json.Nodes;
using Estimates.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestSupport;
using static Estimates.IntegrationTests.EstimateTestHelpers;

namespace Estimates.IntegrationTests;

/// <summary>End-to-end behaviour of the estimate mutations and queries through the real endpoint.</summary>
public sealed class EstimateLifecycleTests
{
    private readonly EstimatesApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public EstimateLifecycleTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    [Fact]
    public async Task Create_add_update_remove_and_approve_an_estimate()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);

        JsonNode draft = await CreateEstimateAsync(_client, token);
        Assert.Equal("DRAFT", draft["status"]!.GetValue<string>());
        Assert.Equal(1, draft["version"]!.GetValue<int>());
        Assert.Empty(draft["lineItems"]!.AsArray());

        JsonNode withFraming = await AddLineItemAsync(_client, token, draft, "Timber framing", 10m, 45.50m, 10m);
        JsonNode withLabour = await AddLineItemAsync(_client, token, withFraming, "Labour", 8m, 95m, 10m);
        Assert.Equal(1336.50m, withLabour["total"]!.GetValue<decimal>());
        Assert.Equal(3, withLabour["version"]!.GetValue<int>());

        // Update the first line item.
        string framingId = withLabour["lineItems"]![0]!["id"]!.GetValue<string>();
        var updateVariables = new
        {
            input = new
            {
                estimateId = withLabour["id"]!.GetValue<string>(),
                lineItemId = framingId,
                expectedVersion = 3,
                description = "Timber framing (treated)",
                quantity = 10m,
                unitPrice = 50m,
                taxRatePercent = 10m,
            },
        };
        GraphQLResponse updateResponse = await _client.SendAsync(Operations.UpdateLineItem, updateVariables, token, TestContext.Current.CancellationToken);
        JsonNode updated = RequirePayloadEstimate(updateResponse, "updateLineItem");
        Assert.Equal(1386.00m, updated["total"]!.GetValue<decimal>());

        // Remove the labour line.
        string labourId = updated["lineItems"]![1]!["id"]!.GetValue<string>();
        var removeVariables = new { input = new { estimateId = updated["id"]!.GetValue<string>(), lineItemId = labourId, expectedVersion = 4 } };
        GraphQLResponse removeResponse = await _client.SendAsync(Operations.RemoveLineItem, removeVariables, token, TestContext.Current.CancellationToken);
        JsonNode afterRemove = RequirePayloadEstimate(removeResponse, "removeLineItem");
        Assert.Single(afterRemove["lineItems"]!.AsArray());
        Assert.Equal(550.00m, afterRemove["total"]!.GetValue<decimal>());

        JsonNode approved = await ApproveAsync(_client, token, afterRemove);
        Assert.Equal("APPROVED", approved["status"]!.GetValue<string>());
        Assert.NotNull(approved["approvedAt"]);

        // Query it back: the persisted state matches.
        GraphQLResponse queried = await _client.SendAsync(Operations.GetEstimate, new { id = approved["id"]!.GetValue<string>() }, token, TestContext.Current.CancellationToken);
        Assert.Null(queried.Errors);
        Assert.Equal("APPROVED", queried.Data!["estimate"]!["status"]!.GetValue<string>());
        Assert.Equal(550.00m, queried.Data!["estimate"]!["total"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Approved_estimate_rejects_further_edits_with_stable_code()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, token);
        JsonNode withItem = await AddLineItemAsync(_client, token, draft, "Roofing", 1m, 1000m, 10m);
        JsonNode approved = await ApproveAsync(_client, token, withItem);

        var variables = new
        {
            input = new
            {
                estimateId = approved["id"]!.GetValue<string>(),
                expectedVersion = approved["version"]!.GetValue<int>(),
                description = "Late addition",
                quantity = 1m,
                unitPrice = 1m,
                taxRatePercent = 0m,
            },
        };
        GraphQLResponse response = await _client.SendAsync(Operations.AddLineItem, variables, token, TestContext.Current.CancellationToken);

        JsonNode error = RequireBusinessError(response, "addLineItem");
        Assert.Equal("InvalidEstimateStateError", error["__typename"]!.GetValue<string>());
        Assert.Equal("ESTIMATE_NOT_EDITABLE", error["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0, 10, "quantity")]
    [InlineData(-5, 10, "quantity")]
    [InlineData(1, -0.01, "unitPrice")]
    public async Task Invalid_quantity_or_price_returns_validation_error(decimal quantity, decimal unitPrice, string expectedField)
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, token);

        var variables = new
        {
            input = new
            {
                estimateId = draft["id"]!.GetValue<string>(),
                expectedVersion = 1,
                description = "Bad line",
                quantity,
                unitPrice,
                taxRatePercent = 10m,
            },
        };
        GraphQLResponse response = await _client.SendAsync(Operations.AddLineItem, variables, token, TestContext.Current.CancellationToken);

        JsonNode error = RequireBusinessError(response, "addLineItem");
        Assert.Equal("VALIDATION_FAILED", error["code"]!.GetValue<string>());
        Assert.Equal(expectedField, error["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unsupported_currency_returns_validation_error()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        var variables = new { input = new { customerName = "Acme", currencyCode = "XXX" } };

        GraphQLResponse response = await _client.SendAsync(Operations.CreateEstimate, variables, token, TestContext.Current.CancellationToken);

        JsonNode error = RequireBusinessError(response, "createEstimate");
        Assert.Equal("currencyCode", error["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stale_expected_version_returns_concurrency_conflict()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, token);
        await AddLineItemAsync(_client, token, draft, "First", 1m, 10m, 10m);

        // "draft" still holds version 1, but the estimate is now at version 2.
        var variables = new { input = new { estimateId = draft["id"]!.GetValue<string>(), expectedVersion = 1 } };
        GraphQLResponse response = await _client.SendAsync(Operations.ApproveEstimate, variables, token, TestContext.Current.CancellationToken);

        JsonNode error = RequireBusinessError(response, "approveEstimate");
        Assert.Equal("CONCURRENCY_CONFLICT", error["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Approving_an_empty_estimate_is_rejected()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, token);

        var variables = new { input = new { estimateId = draft["id"]!.GetValue<string>(), expectedVersion = 1 } };
        GraphQLResponse response = await _client.SendAsync(Operations.ApproveEstimate, variables, token, TestContext.Current.CancellationToken);

        JsonNode error = RequireBusinessError(response, "approveEstimate");
        Assert.Equal("ESTIMATE_HAS_NO_LINE_ITEMS", error["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Database_enforces_optimistic_concurrency_even_if_application_check_is_bypassed()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ReadWriteScopes);
        JsonNode draft = await CreateEstimateAsync(_client, token);
        Guid estimateId = Guid.Parse(draft["id"]!.GetValue<string>());

        // Two independent contexts load the same version, then both try to save.
        await using AsyncServiceScope firstScope = _factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = _factory.Services.CreateAsyncScope();
        EstimatesDbContext first = firstScope.ServiceProvider.GetRequiredService<EstimatesDbContext>();
        EstimatesDbContext second = secondScope.ServiceProvider.GetRequiredService<EstimatesDbContext>();
        Domain.Estimate firstCopy = await first.Estimates.Include(e => e.LineItems).SingleAsync(e => e.Id == estimateId, TestContext.Current.CancellationToken);
        Domain.Estimate secondCopy = await second.Estimates.Include(e => e.LineItems).SingleAsync(e => e.Id == estimateId, TestContext.Current.CancellationToken);

        firstCopy.AddLineItem("From first", 1m, 1m, 0m, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        secondCopy.AddLineItem("From second", 1m, 1m, 0m, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}

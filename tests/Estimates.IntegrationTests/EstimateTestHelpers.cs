using System.Text.Json.Nodes;
using TestSupport;

namespace Estimates.IntegrationTests;

internal static class EstimateTestHelpers
{
    public static readonly string[] ReadWriteScopes = ["estimates:read", "estimates:write"];

    public static string NewTenantId()
    {
        return $"tenant-{Guid.NewGuid():N}";
    }

    public static async Task<JsonNode> CreateEstimateAsync(GraphQLTestClient client, string token, string customerName = "Acme Builders")
    {
        var variables = new { input = new { customerName, customerEmail = "accounts@acme.example", currencyCode = "AUD" } };
        GraphQLResponse response = await client.SendAsync(Operations.CreateEstimate, variables, token, TestContext.Current.CancellationToken);
        return RequirePayloadEstimate(response, "createEstimate");
    }

    public static async Task<JsonNode> AddLineItemAsync(
        GraphQLTestClient client,
        string token,
        JsonNode estimate,
        string description,
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent)
    {
        var variables = new
        {
            input = new
            {
                estimateId = estimate["id"]!.GetValue<string>(),
                expectedVersion = estimate["version"]!.GetValue<int>(),
                description,
                quantity,
                unitPrice,
                taxRatePercent,
            },
        };
        GraphQLResponse response = await client.SendAsync(Operations.AddLineItem, variables, token, TestContext.Current.CancellationToken);
        return RequirePayloadEstimate(response, "addLineItem");
    }

    public static async Task<JsonNode> ApproveAsync(GraphQLTestClient client, string token, JsonNode estimate)
    {
        var variables = new
        {
            input = new
            {
                estimateId = estimate["id"]!.GetValue<string>(),
                expectedVersion = estimate["version"]!.GetValue<int>(),
            },
        };
        GraphQLResponse response = await client.SendAsync(Operations.ApproveEstimate, variables, token, TestContext.Current.CancellationToken);
        return RequirePayloadEstimate(response, "approveEstimate");
    }

    /// <summary>Asserts there are no GraphQL or business errors and returns payload.estimate.</summary>
    public static JsonNode RequirePayloadEstimate(GraphQLResponse response, string mutationField)
    {
        Assert.Null(response.Errors);
        JsonNode payload = response.Data![mutationField]!;
        Assert.Null(payload["errors"]);
        return payload["estimate"]!;
    }

    /// <summary>Returns the first business error from payload.errors.</summary>
    public static JsonNode RequireBusinessError(GraphQLResponse response, string mutationField)
    {
        Assert.Null(response.Errors);
        JsonNode payload = response.Data![mutationField]!;
        Assert.Null(payload["estimate"]);
        JsonArray errors = payload["errors"]!.AsArray();
        return Assert.Single(errors)!;
    }
}

using System.Net;
using System.Text.Json.Nodes;
using Accounting.Application;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestSupport;

namespace Accounting.IntegrationTests;

public sealed class ExportTests
{
    private const string ExportEstimate = """
        mutation ExportEstimate($input: ExportEstimateInput!) {
          exportEstimate(input: $input) {
            export { id estimateId provider status externalReference exportedTotal currencyCode attemptCount lastErrorCode }
            errors { __typename ... on UserError { code message } }
          }
        }
        """;

    private const string GetExport = """
        query GetExport($estimateId: UUID!) {
          estimateExport(estimateId: $estimateId) { id status externalReference attemptCount lastErrorCode }
        }
        """;

    private const string ListExports = """
        query { estimateExports(first: 10) { nodes { id estimateId status } pageInfo { hasNextPage } } }
        """;

    private static readonly string[] ExportScopes = ["accounting:export", "accounting:read"];

    private readonly AccountingApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public ExportTests(AccountingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    private static string NewTenantId()
    {
        return $"tenant-{Guid.NewGuid():N}";
    }

    private Task<GraphQLResponse> ExportAsync(Guid estimateId, string token)
    {
        var variables = new { input = new { estimateId } };
        return _client.SendAsync(ExportEstimate, variables, token, TestContext.Current.CancellationToken);
    }

    private static JsonNode RequireExport(GraphQLResponse response)
    {
        Assert.Null(response.Errors);
        JsonNode payload = response.Data!["exportEstimate"]!;
        Assert.Null(payload["errors"]);
        return payload["export"]!;
    }

    private static JsonNode RequireBusinessError(GraphQLResponse response)
    {
        Assert.Null(response.Errors);
        JsonNode payload = response.Data!["exportEstimate"]!;
        Assert.Null(payload["export"]);
        return Assert.Single(payload["errors"]!.AsArray())!;
    }

    private async Task<int> CountExportRowsAsync(Guid estimateId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AccountingDbContext dbContext = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await dbContext.EstimateExports.CountAsync(export => export.EstimateId == estimateId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exports_approved_estimate_and_status_can_be_queried()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot estimate = FakeEstimatesServiceClient.CreateEstimate();
        _factory.Estimates.AddEstimate(tenantId, estimate);

        JsonNode export = RequireExport(await ExportAsync(estimate.Id, token));

        Assert.Equal("SUCCEEDED", export["status"]!.GetValue<string>());
        Assert.Equal("MOCK", export["provider"]!.GetValue<string>());
        Assert.StartsWith("MOCK-INV-", export["externalReference"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(500.50m, export["exportedTotal"]!.GetValue<decimal>());

        GraphQLResponse status = await _client.SendAsync(GetExport, new { estimateId = estimate.Id }, token, TestContext.Current.CancellationToken);
        Assert.Null(status.Errors);
        Assert.Equal("SUCCEEDED", status.Data!["estimateExport"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Repeated_export_requests_are_idempotent()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot estimate = FakeEstimatesServiceClient.CreateEstimate();
        _factory.Estimates.AddEstimate(tenantId, estimate);

        JsonNode first = RequireExport(await ExportAsync(estimate.Id, token));
        JsonNode second = RequireExport(await ExportAsync(estimate.Id, token));

        Assert.Equal(first["id"]!.GetValue<string>(), second["id"]!.GetValue<string>());
        Assert.Equal(first["externalReference"]!.GetValue<string>(), second["externalReference"]!.GetValue<string>());
        Assert.Equal(1, second["attemptCount"]!.GetValue<int>());
        Assert.Equal(1, await CountExportRowsAsync(estimate.Id));
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_create_exactly_one_export()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot estimate = FakeEstimatesServiceClient.CreateEstimate();
        _factory.Estimates.AddEstimate(tenantId, estimate);

        GraphQLResponse[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ExportAsync(estimate.Id, token)));

        // Every request either returns the single export or says it is in progress; none duplicates it.
        foreach (GraphQLResponse response in responses)
        {
            Assert.Null(response.Errors);
            JsonNode payload = response.Data!["exportEstimate"]!;
            if (payload["errors"] is JsonArray errors)
            {
                Assert.Equal("EXPORT_IN_PROGRESS", errors[0]!["code"]!.GetValue<string>());
            }
        }

        Assert.Equal(1, await CountExportRowsAsync(estimate.Id));
        GraphQLResponse status = await _client.SendAsync(GetExport, new { estimateId = estimate.Id }, token, TestContext.Current.CancellationToken);
        Assert.Equal("SUCCEEDED", status.Data!["estimateExport"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unapproved_estimate_is_rejected()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot draft = FakeEstimatesServiceClient.CreateEstimate(status: "DRAFT");
        _factory.Estimates.AddEstimate(tenantId, draft);

        JsonNode error = RequireBusinessError(await ExportAsync(draft.Id, token));

        Assert.Equal("EstimateNotApprovedError", error["__typename"]!.GetValue<string>());
        Assert.Equal("ESTIMATE_NOT_APPROVED", error["code"]!.GetValue<string>());
        Assert.Equal(0, await CountExportRowsAsync(draft.Id));
    }

    [Fact]
    public async Task Unknown_estimate_is_rejected()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ExportScopes);

        JsonNode error = RequireBusinessError(await ExportAsync(Guid.NewGuid(), token));

        Assert.Equal("ESTIMATE_NOT_FOUND", error["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Estimates_service_outage_returns_typed_error()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot estimate = FakeEstimatesServiceClient.CreateEstimate();
        _factory.Estimates.AddEstimate(tenantId, estimate);
        _factory.Estimates.SimulateOutageFor(estimate.Id);

        JsonNode error = RequireBusinessError(await ExportAsync(estimate.Id, token));

        Assert.Equal("ESTIMATES_SERVICE_UNAVAILABLE", error["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Mock_provider_failure_is_recorded_and_a_later_retry_succeeds()
    {
        string tenantId = NewTenantId();
        string token = _factory.CreateUserToken(tenantId, ExportScopes);
        EstimateSnapshot failing = FakeEstimatesServiceClient.CreateEstimate(customerName: "Acme MOCK-FAIL");
        _factory.Estimates.AddEstimate(tenantId, failing);

        JsonNode failed = RequireExport(await ExportAsync(failing.Id, token));
        Assert.Equal("FAILED", failed["status"]!.GetValue<string>());
        Assert.Equal("ACCOUNTING_PROVIDER_REJECTED", failed["lastErrorCode"]!.GetValue<string>());
        Assert.Null(failed["externalReference"]);

        GraphQLResponse status = await _client.SendAsync(GetExport, new { estimateId = failing.Id }, token, TestContext.Current.CancellationToken);
        Assert.Equal("FAILED", status.Data!["estimateExport"]!["status"]!.GetValue<string>());

        // The problem is fixed (the provider would now accept it); exporting again retries the same record.
        _factory.Estimates.AddEstimate(tenantId, failing with { CustomerName = "Acme Builders" });
        JsonNode retried = RequireExport(await ExportAsync(failing.Id, token));
        Assert.Equal("SUCCEEDED", retried["status"]!.GetValue<string>());
        Assert.Equal(failed["id"]!.GetValue<string>(), retried["id"]!.GetValue<string>());
        Assert.Equal(2, retried["attemptCount"]!.GetValue<int>());
        Assert.Equal(1, await CountExportRowsAsync(failing.Id));
    }

    [Fact]
    public async Task Exports_are_isolated_by_tenant()
    {
        string ownerTenant = NewTenantId();
        string ownerToken = _factory.CreateUserToken(ownerTenant, ExportScopes);
        string otherToken = _factory.CreateUserToken(NewTenantId(), ExportScopes);
        EstimateSnapshot estimate = FakeEstimatesServiceClient.CreateEstimate();
        _factory.Estimates.AddEstimate(ownerTenant, estimate);
        RequireExport(await ExportAsync(estimate.Id, ownerToken));

        GraphQLResponse otherStatus = await _client.SendAsync(GetExport, new { estimateId = estimate.Id }, otherToken, TestContext.Current.CancellationToken);
        GraphQLResponse otherList = await _client.SendAsync(ListExports, null, otherToken, TestContext.Current.CancellationToken);
        JsonNode otherExportAttempt = RequireBusinessError(await ExportAsync(estimate.Id, otherToken));

        Assert.Null(otherStatus.Data!["estimateExport"]);
        Assert.Empty(otherList.Data!["estimateExports"]!["nodes"]!.AsArray());
        Assert.Equal("ESTIMATE_NOT_FOUND", otherExportAttempt["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected_with_401()
    {
        GraphQLResponse response = await ExportAsync(Guid.NewGuid(), token: null!);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_token_cannot_export()
    {
        string token = _factory.CreateUserToken(NewTenantId(), "accounting:read");

        GraphQLResponse response = await ExportAsync(Guid.NewGuid(), token);

        Assert.Equal("AUTH_NOT_AUTHORIZED", response.FirstErrorCode);
    }

    [Fact]
    public async Task Estimates_token_cannot_call_accounting_api()
    {
        // Correct issuer and signature, but issued for the Estimates API audience.
        string token = _factory.Tokens.CreateUserToken("estimates-api", NewTenantId(), ExportScopes);

        GraphQLResponse response = await ExportAsync(Guid.NewGuid(), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

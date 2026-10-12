using System.Net.Http.Json;
using System.Text.Json;
using Accounting.Application;
using Accounting.Domain;
using Microsoft.Extensions.Logging;

namespace Accounting.Infrastructure.EstimatesService;

/// <summary>
/// Reads estimates through the Estimates service's public GraphQL API, never its database.
/// Uses the service-to-service query "estimateForIntegration", which requires the
/// estimates:integration.read scope that only this service's identity holds.
/// </summary>
public sealed class EstimatesServiceClient : IEstimatesServiceClient
{
    // The operation lives in EstimateForExport.graphql (an embedded resource) so the Estimates
    // service's tests can execute exactly the same document as a consumer contract test.
    private static readonly Lazy<string> EstimateQuery = new(LoadEmbeddedQuery);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<EstimatesServiceClient> _logger;

    public EstimatesServiceClient(HttpClient httpClient, ILogger<EstimatesServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<EstimateSnapshot?> GetEstimateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        var request = new GraphQLRequest(EstimateQuery.Value, new Dictionary<string, object> { ["tenantId"] = tenantId, ["id"] = estimateId });

        GraphQLResponse? response;
        try
        {
            using HttpResponseMessage httpResponse = await _httpClient.PostAsJsonAsync("/graphql", request, JsonOptions, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Estimates service returned HTTP {StatusCode}", (int)httpResponse.StatusCode);
                throw new EstimatesServiceUnavailableException($"The Estimates service returned HTTP {(int)httpResponse.StatusCode}.");
            }

            response = await httpResponse.Content.ReadFromJsonAsync<GraphQLResponse>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new EstimatesServiceUnavailableException("The Estimates service could not be reached.", exception);
        }
        catch (Polly.ExecutionRejectedException exception)
        {
            // Raised by the resilience pipeline: a timeout was exceeded or the circuit breaker is open.
            throw new EstimatesServiceUnavailableException("The Estimates service did not respond in time or is temporarily unavailable.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EstimatesServiceUnavailableException("The Estimates service did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new EstimatesServiceUnavailableException("The Estimates service returned an unreadable response.", exception);
        }

        // GraphQL can return HTTP 200 with errors (for example AUTH_NOT_AUTHORIZED if this service's
        // scope was misconfigured). Treat any error as a failed dependency, and log the codes.
        if (response?.Errors is { Count: > 0 })
        {
            string codes = string.Join(", ", response.Errors.Select(error => error.Extensions?.Code ?? "UNKNOWN"));
            _logger.LogError("Estimates service returned GraphQL errors: {ErrorCodes}", codes);
            throw new EstimatesServiceUnavailableException($"The Estimates service returned errors: {codes}.");
        }

        EstimateDto? estimate = response?.Data?.EstimateForIntegration;
        if (estimate is null)
        {
            return null;
        }

        var lines = estimate.LineItems
            .Select(line => new EstimateLineSnapshot(
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.TaxRatePercent,
                line.Subtotal,
                line.TaxAmount,
                line.Total))
            .ToList();

        return new EstimateSnapshot(
            estimate.Id,
            estimate.Status,
            estimate.CurrencyCode,
            estimate.Customer.Name,
            estimate.Customer.Email,
            estimate.Subtotal,
            estimate.TaxTotal,
            estimate.Total,
            lines);
    }

    private static string LoadEmbeddedQuery()
    {
        const string ResourceName = "Accounting.Infrastructure.EstimatesService.EstimateForExport.graphql";
        using Stream? stream = typeof(EstimatesServiceClient).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded GraphQL operation '{ResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Private DTOs matching the Estimates GraphQL response. They are this client's own copy of the
    // contract, not shared types.
    private sealed record GraphQLRequest(string Query, Dictionary<string, object> Variables);

    private sealed class GraphQLResponse
    {
        public ResponseData? Data { get; set; }

        public List<GraphQLError>? Errors { get; set; }
    }

    private sealed class ResponseData
    {
        public EstimateDto? EstimateForIntegration { get; set; }
    }

    private sealed class GraphQLError
    {
        public string Message { get; set; } = string.Empty;

        public ErrorExtensions? Extensions { get; set; }
    }

    private sealed class ErrorExtensions
    {
        public string? Code { get; set; }
    }

    private sealed class EstimateDto
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public decimal Subtotal { get; set; }

        public decimal TaxTotal { get; set; }

        public decimal Total { get; set; }

        public CustomerDto Customer { get; set; } = new();

        public List<LineItemDto> LineItems { get; set; } = [];
    }

    private sealed class CustomerDto
    {
        public string Name { get; set; } = string.Empty;

        public string? Email { get; set; }
    }

    private sealed class LineItemDto
    {
        public string Description { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal TaxRatePercent { get; set; }

        public decimal Subtotal { get; set; }

        public decimal TaxAmount { get; set; }

        public decimal Total { get; set; }
    }
}

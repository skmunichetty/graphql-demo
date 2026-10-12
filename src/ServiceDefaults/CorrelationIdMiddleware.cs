using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults;

/// <summary>
/// Gives every request a correlation ID so that all log lines for one request (and for the calls
/// it makes to other services) can be found together.
/// - If the caller sends a well-formed X-Correlation-ID header, it is reused.
/// - Otherwise the W3C trace ID of the current request is used, so logs and traces line up.
/// The ID is returned in the response header and added to every log entry's scope.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = GetOrCreateCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Baggage travels with outgoing HTTP calls (W3C baggage header), so downstream services
        // can log the same correlation ID.
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetBaggage("correlation.id", correlationId);

        // A message-template scope is rendered as "CorrelationId:abc" by the simple console formatter
        // and as a structured "CorrelationId" property by the JSON formatter and OpenTelemetry.
        using (_logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        string? incoming = context.Request.Headers[HeaderName].FirstOrDefault();

        // Only accept short, safe values: the header is client input and ends up in logs.
        if (!string.IsNullOrEmpty(incoming) && SafeCorrelationId().IsMatch(incoming))
        {
            return incoming;
        }

        if (Activity.Current is not null)
        {
            return Activity.Current.TraceId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeCorrelationId();
}

/// <summary>
/// Adds the current request's correlation ID to outgoing HTTP calls, so the called service
/// logs the same ID. Register it on typed HttpClients that call other services.
/// </summary>
public sealed class CorrelationIdPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdPropagationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(correlationId) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

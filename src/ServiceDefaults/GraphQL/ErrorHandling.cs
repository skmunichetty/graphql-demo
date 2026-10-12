using System.Diagnostics;
using System.Diagnostics.Metrics;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Resolvers;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.GraphQL;

/// <summary>
/// Hides unexpected exceptions from clients. Business errors never reach this filter because
/// mutation conventions turn them into typed payload errors. Anything else with an exception is
/// a bug or an infrastructure failure: the client gets a generic message, a stable code and the
/// trace ID, and the details go to the logs (see <see cref="GraphQLErrorLoggingListener"/>).
/// </summary>
public sealed class UnexpectedErrorFilter : IErrorFilter
{
    public const string InternalErrorCode = "INTERNAL_ERROR";

    public IError OnError(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // Errors without an exception were created deliberately (validation, authorization,
        // cost limits...) and already carry a meaningful message and code.
        if (error.Exception is null)
        {
            return error;
        }

        string? traceId = Activity.Current?.TraceId.ToString();
        return error
            .WithMessage("An unexpected error occurred. Quote the traceId if you contact support.")
            .WithCode(InternalErrorCode)
            .SetExtension("traceId", traceId);
    }
}

/// <summary>
/// Makes GraphQL errors visible to operators. GraphQL usually returns HTTP 200 even when the
/// response contains errors, so HTTP status metrics alone would hide them. This listener logs
/// every error and counts them by kind and code in the "graphql.errors" metric.
/// </summary>
public sealed class GraphQLErrorLoggingListener : ExecutionDiagnosticEventListener
{
    public const string MeterName = "ServiceDefaults.GraphQL";

    private static readonly Meter GraphQLMeter = new(MeterName);
    private static readonly Counter<long> ErrorCounter = GraphQLMeter.CreateCounter<long>(
        "graphql.errors",
        description: "GraphQL errors returned to clients, including those sent with HTTP 200.");

    private readonly ILogger<GraphQLErrorLoggingListener> _logger;

    public GraphQLErrorLoggingListener(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<GraphQLErrorLoggingListener>();
    }

    public override void RequestError(RequestContext context, Exception error)
    {
        CountError("request", UnexpectedErrorFilter.InternalErrorCode);
        _logger.LogError(error, "GraphQL request failed with an unexpected exception");
    }

    public override void RequestError(RequestContext context, IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        LogError("request", error);
    }

    public override void ValidationErrors(RequestContext context, IReadOnlyList<IError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        foreach (IError error in errors)
        {
            LogError("validation", error);
        }
    }

    public override void ResolverError(IMiddlewareContext context, IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        LogError("resolver", error);
    }

    public override void ResolverError(RequestContext context, ISelection selection, IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        LogError("resolver", error);
    }

    private void LogError(string kind, IError error)
    {
        string code = error.Code ?? "UNKNOWN";
        CountError(kind, code);

        if (error.Exception is not null)
        {
            _logger.LogError(
                error.Exception,
                "GraphQL {ErrorKind} error {ErrorCode} at {ErrorPath}",
                kind,
                code,
                error.Path?.ToString());
            return;
        }

        // Expected errors (bad queries, missing permissions) are warnings, not failures of this service.
        _logger.LogWarning(
            "GraphQL {ErrorKind} error {ErrorCode} at {ErrorPath}: {ErrorMessage}",
            kind,
            code,
            error.Path?.ToString(),
            error.Message);
    }

    private static void CountError(string kind, string code)
    {
        ErrorCounter.Add(
            1,
            new KeyValuePair<string, object?>("graphql.error.kind", kind),
            new KeyValuePair<string, object?>("graphql.error.code", code));
    }
}

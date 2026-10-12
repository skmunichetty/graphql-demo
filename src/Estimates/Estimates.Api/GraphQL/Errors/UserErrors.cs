using Estimates.Application;
using Estimates.Domain;

namespace Estimates.Api.GraphQL.Errors;

// Business errors are part of the schema. Hot Chocolate's mutation conventions catch the listed
// exception types and return them in the payload's "errors" field instead of the top-level
// GraphQL "errors" array. Each error class takes its exception in the constructor; only the
// properties declared here are exposed (never stack traces or inner exceptions).

/// <summary>Shared shape of every business error: a human-readable message and a stable code.</summary>
[GraphQLName("UserError")]
[GraphQLDescription("A business rule or validation failure the client can act on.")]
public interface IUserError
{
    [GraphQLDescription("Human-readable description. Do not parse it; it may change.")]
    string Message { get; }

    [GraphQLDescription("Stable machine-readable error code, for example VALIDATION_FAILED.")]
    string Code { get; }
}

public sealed class ValidationError : IUserError
{
    public ValidationError(EstimateValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
        Field = exception.Field;
    }

    public string Message { get; }

    public string Code { get; }

    [GraphQLDescription("The input field that failed validation.")]
    public string Field { get; }
}

public sealed class EstimateNotFoundError : IUserError
{
    public EstimateNotFoundError(EstimateNotFoundException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
        EstimateId = exception.EstimateId;
    }

    public string Message { get; }

    public string Code { get; }

    public Guid EstimateId { get; }
}

public sealed class LineItemNotFoundError : IUserError
{
    public LineItemNotFoundError(LineItemNotFoundException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
        LineItemId = exception.LineItemId;
    }

    public string Message { get; }

    public string Code { get; }

    public Guid LineItemId { get; }
}

[GraphQLDescription("The estimate's status does not allow this operation. Codes: ESTIMATE_NOT_EDITABLE, ESTIMATE_HAS_NO_LINE_ITEMS.")]
public sealed class InvalidEstimateStateError : IUserError
{
    public InvalidEstimateStateError(InvalidEstimateStateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
    }

    public string Message { get; }

    public string Code { get; }
}

[GraphQLDescription("The estimate changed since you read it. Re-query it and retry with the current version.")]
public sealed class ConcurrencyConflictError : IUserError
{
    public ConcurrencyConflictError(ConcurrencyConflictException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
        EstimateId = exception.EstimateId;
        ExpectedVersion = exception.ExpectedVersion;
        CurrentVersion = exception.CurrentVersion;
    }

    public string Message { get; }

    public string Code { get; }

    public Guid EstimateId { get; }

    public int ExpectedVersion { get; }

    [GraphQLDescription("The stored version, when known.")]
    public int? CurrentVersion { get; }
}

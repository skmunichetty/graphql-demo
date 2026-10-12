using Accounting.Domain;

namespace Accounting.Api.GraphQL;

/// <summary>Shared shape of every business error: a human-readable message and a stable code.</summary>
[GraphQLName("UserError")]
[GraphQLDescription("A business rule failure the client can act on.")]
public interface IUserError
{
    [GraphQLDescription("Human-readable description. Do not parse it; it may change.")]
    string Message { get; }

    [GraphQLDescription("Stable machine-readable error code.")]
    string Code { get; }
}

[GraphQLDescription("The estimate does not exist in your tenant.")]
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

[GraphQLDescription("Only approved estimates can be exported.")]
public sealed class EstimateNotApprovedError : IUserError
{
    public EstimateNotApprovedError(EstimateNotApprovedException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
        EstimateId = exception.EstimateId;
        CurrentStatus = exception.CurrentStatus;
    }

    public string Message { get; }

    public string Code { get; }

    public Guid EstimateId { get; }

    [GraphQLDescription("The estimate's status in the Estimates service, for example DRAFT.")]
    public string CurrentStatus { get; }
}

[GraphQLDescription("The estimate is being exported by another request. Query estimateExport for its status.")]
public sealed class ExportInProgressError : IUserError
{
    public ExportInProgressError(ExportInProgressException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
    }

    public string Message { get; }

    public string Code { get; }
}

[GraphQLDescription("The Estimates service could not be reached. Nothing was exported; retry later.")]
public sealed class EstimatesServiceUnavailableError : IUserError
{
    public EstimatesServiceUnavailableError(EstimatesServiceUnavailableException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
    }

    public string Message { get; }

    public string Code { get; }
}

[GraphQLDescription("The requested accounting provider is not configured on this server.")]
public sealed class UnsupportedProviderError : IUserError
{
    public UnsupportedProviderError(UnsupportedProviderException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = exception.Message;
        Code = exception.Code;
    }

    public string Message { get; }

    public string Code { get; }
}

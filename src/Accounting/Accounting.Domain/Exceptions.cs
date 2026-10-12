namespace Accounting.Domain;

/// <summary>
/// Base type for expected failures that the API returns as typed GraphQL errors with a stable code.
/// This deliberately duplicates the Estimates service's base type: services do not share domain code.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class EstimateNotFoundException : DomainException
{
    public EstimateNotFoundException(Guid estimateId)
        : base(ErrorCodes.EstimateNotFound, $"Estimate '{estimateId}' was not found.")
    {
        EstimateId = estimateId;
    }

    public Guid EstimateId { get; }
}

public sealed class EstimateNotApprovedException : DomainException
{
    public EstimateNotApprovedException(Guid estimateId, string currentStatus)
        : base(ErrorCodes.EstimateNotApproved, $"Estimate '{estimateId}' is {currentStatus}. Only approved estimates can be exported.")
    {
        EstimateId = estimateId;
        CurrentStatus = currentStatus;
    }

    public Guid EstimateId { get; }

    public string CurrentStatus { get; }
}

/// <summary>Another request is exporting this estimate right now.</summary>
public sealed class ExportInProgressException : DomainException
{
    public ExportInProgressException(Guid exportId)
        : base(ErrorCodes.ExportInProgress, "This estimate is already being exported. Query the export status and try again later.")
    {
        ExportId = exportId;
    }

    public Guid ExportId { get; }
}

/// <summary>The Estimates service could not be reached or returned an unusable response. Safe to retry later.</summary>
public sealed class EstimatesServiceUnavailableException : DomainException
{
    public EstimatesServiceUnavailableException(string message, Exception? innerException = null)
        : base(ErrorCodes.EstimatesServiceUnavailable, message, innerException)
    {
    }
}

public sealed class UnsupportedProviderException : DomainException
{
    public UnsupportedProviderException(AccountingProvider provider)
        : base(ErrorCodes.UnsupportedProvider, $"Accounting provider '{provider}' is not configured.")
    {
        Provider = provider;
    }

    public AccountingProvider Provider { get; }
}

/// <summary>
/// Thrown by provider implementations when the accounting system rejects or cannot process an export.
/// The export service records it on the export (status FAILED) rather than returning it as an error.
/// </summary>
public sealed class AccountingProviderException : Exception
{
    public AccountingProviderException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

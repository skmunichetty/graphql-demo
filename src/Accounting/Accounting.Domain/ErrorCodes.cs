namespace Accounting.Domain;

/// <summary>
/// Stable, machine-readable error codes returned by the Accounting service.
/// Never rename or reuse a code; add new ones and document them in the client guide.
/// </summary>
public static class ErrorCodes
{
    public const string EstimateNotFound = "ESTIMATE_NOT_FOUND";
    public const string EstimateNotApproved = "ESTIMATE_NOT_APPROVED";
    public const string ExportInProgress = "EXPORT_IN_PROGRESS";
    public const string EstimatesServiceUnavailable = "ESTIMATES_SERVICE_UNAVAILABLE";
    public const string UnsupportedProvider = "UNSUPPORTED_ACCOUNTING_PROVIDER";

    // Stored on a FAILED export (lastErrorCode), not returned as a mutation error.
    public const string ProviderRejected = "ACCOUNTING_PROVIDER_REJECTED";
    public const string ProviderUnavailable = "ACCOUNTING_PROVIDER_UNAVAILABLE";
    public const string ProviderTimeout = "ACCOUNTING_PROVIDER_TIMEOUT";
}

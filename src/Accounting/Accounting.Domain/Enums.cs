namespace Accounting.Domain;

/// <summary>
/// Accounting systems this service can export to. Milestone 1 has only the mock provider.
/// Xero and QuickBooks will be added as new values (an additive schema change).
/// </summary>
public enum AccountingProvider
{
    Mock = 0,
}

/// <summary>
/// Pending   - the export was recorded and the provider call is in progress (or was interrupted).
/// Succeeded - the provider accepted the invoice; ExternalReference identifies it there.
/// Failed    - the provider call failed; exporting again retries it.
/// </summary>
public enum ExportStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

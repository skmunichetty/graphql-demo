namespace Estimates.Domain;

/// <summary>
/// Stable, machine-readable error codes returned to API clients.
/// These strings are part of the public contract: never rename or reuse a code.
/// Add new codes instead, and document them in docs/client-integration-guide.md.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string EstimateNotFound = "ESTIMATE_NOT_FOUND";
    public const string LineItemNotFound = "LINE_ITEM_NOT_FOUND";
    public const string EstimateNotEditable = "ESTIMATE_NOT_EDITABLE";
    public const string EstimateHasNoLineItems = "ESTIMATE_HAS_NO_LINE_ITEMS";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}

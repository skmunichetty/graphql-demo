namespace Accounting.Application;

/// <summary>
/// The Accounting service's own view of an estimate, read from the Estimates service API.
/// It contains only what an export needs. It is NOT the Estimates service's domain entity:
/// services share contracts (the GraphQL schema), never code or database tables.
/// </summary>
public sealed record EstimateSnapshot(
    Guid Id,
    string Status,
    string CurrencyCode,
    string CustomerName,
    string? CustomerEmail,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    IReadOnlyList<EstimateLineSnapshot> Lines)
{
    /// <summary>The Estimates API's enum value for approved estimates.</summary>
    public const string ApprovedStatus = "APPROVED";

    public bool IsApproved => Status == ApprovedStatus;
}

public sealed record EstimateLineSnapshot(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRatePercent,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total);

/// <summary>
/// What is sent to an accounting provider. Providers translate this into their own invoice format.
/// </summary>
public sealed record AccountingInvoice(
    string TenantId,
    Guid EstimateId,
    string CustomerName,
    string? CustomerEmail,
    string CurrencyCode,
    IReadOnlyList<EstimateLineSnapshot> Lines,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total)
{
    public static AccountingInvoice FromEstimate(string tenantId, EstimateSnapshot estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return new AccountingInvoice(
            tenantId,
            estimate.Id,
            estimate.CustomerName,
            estimate.CustomerEmail,
            estimate.CurrencyCode,
            estimate.Lines,
            estimate.Subtotal,
            estimate.TaxTotal,
            estimate.Total);
    }
}

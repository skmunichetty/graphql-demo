namespace Estimates.Application;

// Commands are plain data describing what the caller wants to do. They never contain a tenant:
// the tenant is passed separately after being derived from the caller's validated token.

public sealed record CreateEstimateCommand(
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string CurrencyCode);

// TaxRatePercent: when null, the configured demo default tax rate (PricingOptions) is used.
public sealed record AddLineItemCommand(
    Guid EstimateId,
    int ExpectedVersion,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal? TaxRatePercent);

public sealed record UpdateLineItemCommand(
    Guid EstimateId,
    Guid LineItemId,
    int ExpectedVersion,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRatePercent);

public sealed record RemoveLineItemCommand(Guid EstimateId, Guid LineItemId, int ExpectedVersion);

public sealed record ApproveEstimateCommand(Guid EstimateId, int ExpectedVersion);

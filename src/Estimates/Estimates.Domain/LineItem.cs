namespace Estimates.Domain;

/// <summary>
/// One priced line on an estimate. Line items are only changed through <see cref="Estimate"/>
/// (the aggregate root) so that totals, status checks and the version number stay consistent.
/// </summary>
public sealed class LineItem
{
    // Required by EF Core.
    private LineItem()
    {
        TenantId = string.Empty;
        Description = string.Empty;
    }

    private LineItem(Guid id, Guid estimateId, string tenantId, int position)
    {
        Id = id;
        EstimateId = estimateId;
        TenantId = tenantId;
        Position = position;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid EstimateId { get; private set; }

    /// <summary>
    /// Copied from the estimate so that line-item queries (for example the DataLoader batch)
    /// can filter by tenant directly, as defence in depth.
    /// </summary>
    public string TenantId { get; private set; }

    /// <summary>Display order within the estimate, starting at 1.</summary>
    public int Position { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal Total { get; private set; }

    internal static LineItem Create(
        Guid estimateId,
        string tenantId,
        int position,
        string? description,
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent)
    {
        var lineItem = new LineItem(Guid.CreateVersion7(), estimateId, tenantId, position);
        lineItem.SetDetails(description, quantity, unitPrice, taxRatePercent);
        return lineItem;
    }

    internal void SetDetails(string? description, decimal quantity, decimal unitPrice, decimal taxRatePercent)
    {
        string validDescription = EstimateRules.ValidateRequiredText(
            description,
            "description",
            EstimateRules.MaxDescriptionLength);
        EstimateRules.ValidateQuantity(quantity);
        EstimateRules.ValidateUnitPrice(unitPrice);
        EstimateRules.ValidateTaxRatePercent(taxRatePercent);

        LineAmounts amounts = PricingCalculator.CalculateLine(quantity, unitPrice, taxRatePercent);

        Description = validDescription;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxRatePercent = taxRatePercent;
        Subtotal = amounts.Subtotal;
        TaxAmount = amounts.TaxAmount;
        Total = amounts.Total;
    }

    internal void MoveToPosition(int position)
    {
        Position = position;
    }
}

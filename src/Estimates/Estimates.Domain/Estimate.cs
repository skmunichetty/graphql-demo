namespace Estimates.Domain;

/// <summary>
/// The estimate aggregate root. It owns its line items and enforces every rule that must hold
/// for the estimate as a whole: status transitions, totals and the version number.
/// </summary>
public sealed class Estimate
{
    private readonly List<LineItem> _lineItems = [];

    // Required by EF Core.
    private Estimate()
    {
        TenantId = string.Empty;
        CurrencyCode = string.Empty;
        Customer = null!;
    }

    private Estimate(Guid id, string tenantId, CustomerDetails customer, string currencyCode, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Customer = customer;
        CurrencyCode = currencyCode;
        Status = EstimateStatus.Draft;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>The owning tenant. Always taken from the caller's validated token, never from client input.</summary>
    public string TenantId { get; private set; }

    public CustomerDetails Customer { get; private set; }

    /// <summary>One ISO 4217 currency per estimate. There is no currency conversion.</summary>
    public string CurrencyCode { get; private set; }

    public EstimateStatus Status { get; private set; }

    // Totals are stored (not only calculated on read) so that lists can show totals
    // without loading every line item.
    public decimal Subtotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>
    /// Optimistic concurrency version. It increases by one on every change and is checked by the
    /// database on save, so two clients cannot silently overwrite each other's edits.
    /// </summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public IReadOnlyList<LineItem> LineItems => _lineItems;

    public static Estimate CreateDraft(
        string tenantId,
        CustomerDetails customer,
        string? currencyCode,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // A programming error, not user input: tenant comes from the validated token.
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(customer);
        string validCurrency = EstimateRules.ValidateCurrencyCode(currencyCode);
        return new Estimate(Guid.CreateVersion7(), tenantId, customer, validCurrency, now);
    }

    public LineItem AddLineItem(
        string? description,
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        DateTimeOffset now)
    {
        EnsureEditable();

        if (_lineItems.Count >= EstimateRules.MaxLineItems)
        {
            throw new EstimateValidationException(
                "lineItems",
                $"An estimate can have at most {EstimateRules.MaxLineItems} line items.");
        }

        int nextPosition = _lineItems.Count + 1;
        LineItem lineItem = LineItem.Create(Id, TenantId, nextPosition, description, quantity, unitPrice, taxRatePercent);
        _lineItems.Add(lineItem);
        RecalculateAndMarkChanged(now);
        return lineItem;
    }

    public LineItem UpdateLineItem(
        Guid lineItemId,
        string? description,
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        DateTimeOffset now)
    {
        EnsureEditable();

        LineItem lineItem = FindLineItem(lineItemId);
        lineItem.SetDetails(description, quantity, unitPrice, taxRatePercent);
        RecalculateAndMarkChanged(now);
        return lineItem;
    }

    public void RemoveLineItem(Guid lineItemId, DateTimeOffset now)
    {
        EnsureEditable();

        LineItem lineItem = FindLineItem(lineItemId);
        _lineItems.Remove(lineItem);

        // Keep positions contiguous (1, 2, 3...) so clients can display them directly.
        int position = 1;
        foreach (LineItem remaining in _lineItems.OrderBy(item => item.Position))
        {
            remaining.MoveToPosition(position);
            position++;
        }

        RecalculateAndMarkChanged(now);
    }

    public void Approve(DateTimeOffset now)
    {
        EnsureEditable();

        if (_lineItems.Count == 0)
        {
            throw new InvalidEstimateStateException(
                ErrorCodes.EstimateHasNoLineItems,
                "An estimate must have at least one line item before it can be approved.");
        }

        Status = EstimateStatus.Approved;
        ApprovedAt = now;
        MarkChanged(now);
    }

    private void EnsureEditable()
    {
        if (Status != EstimateStatus.Draft)
        {
            throw new InvalidEstimateStateException(
                ErrorCodes.EstimateNotEditable,
                $"The estimate is {Status} and can no longer be changed.");
        }
    }

    private LineItem FindLineItem(Guid lineItemId)
    {
        LineItem? lineItem = _lineItems.FirstOrDefault(item => item.Id == lineItemId);
        if (lineItem is null)
        {
            throw new LineItemNotFoundException(lineItemId);
        }

        return lineItem;
    }

    private void RecalculateAndMarkChanged(DateTimeOffset now)
    {
        decimal subtotal = 0m;
        decimal taxTotal = 0m;
        foreach (LineItem lineItem in _lineItems)
        {
            subtotal += lineItem.Subtotal;
            taxTotal += lineItem.TaxAmount;
        }

        Subtotal = subtotal;
        TaxTotal = taxTotal;
        Total = subtotal + taxTotal;
        MarkChanged(now);
    }

    private void MarkChanged(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }
}

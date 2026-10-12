namespace Accounting.Domain;

/// <summary>
/// The record of exporting one estimate to one accounting provider for one tenant.
/// There is at most one export per (tenant, estimate, provider): a unique database index enforces it,
/// which is the foundation of idempotency. Retrying a failed export updates this same record.
/// </summary>
public sealed class EstimateExport
{
    public const int MaxErrorMessageLength = 500;

    // Required by EF Core.
    private EstimateExport()
    {
        TenantId = string.Empty;
        CurrencyCode = string.Empty;
    }

    private EstimateExport(Guid id, string tenantId, Guid estimateId, AccountingProvider provider, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        EstimateId = estimateId;
        Provider = provider;
        CurrencyCode = string.Empty;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string TenantId { get; private set; }

    /// <summary>The estimate's ID in the Estimates service. Not a foreign key: that data lives in another service.</summary>
    public Guid EstimateId { get; private set; }

    public AccountingProvider Provider { get; private set; }

    public ExportStatus Status { get; private set; }

    /// <summary>The invoice identifier in the accounting system, once the export succeeded.</summary>
    public string? ExternalReference { get; private set; }

    // Snapshot of what was sent, so the export record stays meaningful on its own.
    public string CurrencyCode { get; private set; }

    public decimal ExportedTotal { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? LastErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency version: stops two requests retrying the same failed export at once.</summary>
    public int Version { get; private set; }

    public static EstimateExport Start(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        string currencyCode,
        decimal total,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }

        var export = new EstimateExport(Guid.CreateVersion7(), tenantId, estimateId, provider, now);
        export.BeginAttempt(currencyCode, total, now);
        return export;
    }

    /// <summary>
    /// Starts another attempt for a failed export, or for one stuck in Pending (for example because
    /// the service stopped mid-call). Succeeded exports are final and are never re-sent.
    /// </summary>
    public void Retry(string currencyCode, decimal total, DateTimeOffset now)
    {
        if (Status == ExportStatus.Succeeded)
        {
            throw new InvalidOperationException("A succeeded export must not be sent again.");
        }

        BeginAttempt(currencyCode, total, now);
    }

    public bool IsStalePending(DateTimeOffset now, TimeSpan pendingTimeout)
    {
        return Status == ExportStatus.Pending && now - UpdatedAt > pendingTimeout;
    }

    public void MarkSucceeded(string externalReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalReference);
        EnsurePending();

        Status = ExportStatus.Succeeded;
        ExternalReference = externalReference;
        LastErrorCode = null;
        LastErrorMessage = null;
        CompletedAt = now;
        Touch(now);
    }

    public void MarkFailed(string errorCode, string errorMessage, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        EnsurePending();

        Status = ExportStatus.Failed;
        LastErrorCode = errorCode;
        LastErrorMessage = Truncate(errorMessage, MaxErrorMessageLength);
        CompletedAt = null;
        Touch(now);
    }

    private void BeginAttempt(string currencyCode, decimal total, DateTimeOffset now)
    {
        Status = ExportStatus.Pending;
        CurrencyCode = currencyCode;
        ExportedTotal = total;
        AttemptCount++;
        Touch(now);
    }

    private void EnsurePending()
    {
        if (Status != ExportStatus.Pending)
        {
            throw new InvalidOperationException($"Export is {Status}; only a pending export can be completed.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}

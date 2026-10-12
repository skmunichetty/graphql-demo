using Accounting.Domain;
using GreenDonut.Data;

namespace Accounting.Application;

/// <summary>
/// Reads estimates from the Estimates service (over its GraphQL API, with a service identity).
/// Returns null when the estimate does not exist for the tenant.
/// Throws <see cref="EstimatesServiceUnavailableException"/> when the service cannot be reached.
/// </summary>
public interface IEstimatesServiceClient
{
    Task<EstimateSnapshot?> GetEstimateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken);
}

/// <summary>
/// One accounting system (Mock today; Xero or QuickBooks later). Implementations live in
/// Infrastructure and keep any provider OAuth credentials server-side.
/// </summary>
public interface IAccountingProvider
{
    AccountingProvider Provider { get; }

    /// <summary>
    /// Creates the invoice in the accounting system and returns its external reference.
    /// The idempotency key is the same for every attempt of the same export, so a provider that
    /// supports idempotency keys will not create a duplicate if an earlier attempt actually succeeded.
    /// Throws <see cref="AccountingProviderException"/> when the provider rejects or fails the request.
    /// </summary>
    Task<string> ExportInvoiceAsync(AccountingInvoice invoice, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>Write-side persistence for export records (request-scoped, change-tracked).</summary>
public interface IEstimateExportRepository
{
    Task<EstimateExport?> GetForUpdateAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts and saves a new export immediately. Returns false if an export for the same
    /// (tenant, estimate, provider) already exists, which means a concurrent request won the race.
    /// </summary>
    Task<bool> TryInsertAsync(EstimateExport export, CancellationToken cancellationToken);

    /// <summary>Saves changes; returns false if another request changed the export first.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Read-side queries. Every method filters by tenant; safe to call concurrently.</summary>
public interface IEstimateExportReadStore
{
    Task<EstimateExportReadModel?> GetByEstimateAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken);

    Task<EstimateExportReadModel?> GetByIdAsync(string tenantId, Guid exportId, CancellationToken cancellationToken);

    Task<Page<EstimateExportReadModel>> ListAsync(
        string tenantId,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken);
}

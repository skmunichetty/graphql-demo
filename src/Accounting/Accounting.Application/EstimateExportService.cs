using Accounting.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Application;

/// <summary>
/// Exports an approved estimate to an accounting provider, synchronously, exactly once.
///
/// Idempotency: there is one export record per (tenant, estimate, provider), enforced by a unique
/// index. Repeating the request:
///   - after success       -> returns the existing export; the provider is NOT called again.
///   - while in progress   -> EXPORT_IN_PROGRESS error; the client should poll the status.
///   - after a failure     -> retries the same export record (attemptCount increases).
///
/// Limitation (milestone 1): the client waits for the provider call. If the provider is slow or
/// down the request is slow or fails, and nothing retries automatically. Milestone 3 replaces this
/// with an outbox, a background worker and retries (see docs/roadmap.md).
/// </summary>
public sealed class EstimateExportService
{
    private readonly IEstimateExportRepository _repository;
    private readonly IEstimatesServiceClient _estimatesClient;
    private readonly AccountingProviderRegistry _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ExportOptions _options;
    private readonly ILogger<EstimateExportService> _logger;

    public EstimateExportService(
        IEstimateExportRepository repository,
        IEstimatesServiceClient estimatesClient,
        AccountingProviderRegistry providers,
        TimeProvider timeProvider,
        IOptions<ExportOptions> options,
        ILogger<EstimateExportService> logger)
    {
        _repository = repository;
        _estimatesClient = estimatesClient;
        _providers = providers;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EstimateExportReadModel> ExportAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken)
    {
        IAccountingProvider accountingProvider = _providers.Get(provider);

        EstimateExport? existing = await _repository.GetForUpdateAsync(tenantId, estimateId, provider, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == ExportStatus.Succeeded)
            {
                _logger.LogInformation(
                    "Estimate {EstimateId} was already exported as {ExternalReference}; returning the existing export",
                    estimateId,
                    existing.ExternalReference);
                return EstimateExportReadModel.FromExport(existing);
            }

            TimeSpan pendingTimeout = TimeSpan.FromSeconds(_options.PendingTimeoutSeconds);
            if (existing.Status == ExportStatus.Pending && !existing.IsStalePending(_timeProvider.GetUtcNow(), pendingTimeout))
            {
                throw new ExportInProgressException(existing.Id);
            }
        }

        // Always read the estimate's current state from its owner before exporting.
        EstimateSnapshot estimate = await LoadApprovedEstimateAsync(tenantId, estimateId, cancellationToken);

        EstimateExport export;
        if (existing is null)
        {
            export = EstimateExport.Start(tenantId, estimateId, provider, estimate.CurrencyCode, estimate.Total, _timeProvider.GetUtcNow());
            bool inserted = await _repository.TryInsertAsync(export, cancellationToken);
            if (!inserted)
            {
                return await ReturnConcurrentExportAsync(tenantId, estimateId, provider, cancellationToken);
            }
        }
        else
        {
            existing.Retry(estimate.CurrencyCode, estimate.Total, _timeProvider.GetUtcNow());
            bool saved = await _repository.TrySaveChangesAsync(cancellationToken);
            if (!saved)
            {
                // Another request started a retry of the same export first.
                throw new ExportInProgressException(existing.Id);
            }

            export = existing;
        }

        await SendToProviderAsync(accountingProvider, export, AccountingInvoice.FromEstimate(tenantId, estimate));
        return EstimateExportReadModel.FromExport(export);
    }

    private async Task<EstimateSnapshot> LoadApprovedEstimateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        EstimateSnapshot? estimate = await _estimatesClient.GetEstimateAsync(tenantId, estimateId, cancellationToken);
        if (estimate is null)
        {
            throw new EstimateNotFoundException(estimateId);
        }

        if (!estimate.IsApproved)
        {
            throw new EstimateNotApprovedException(estimateId, estimate.Status);
        }

        return estimate;
    }

    /// <summary>
    /// Calls the provider and records the outcome. Deliberate decision: once the export is recorded
    /// as PENDING, the provider call and the final save are NOT cancelled if the client disconnects.
    /// Abandoning half-way would leave the record PENDING even though the provider may have created
    /// the invoice. The call is still bounded by the provider timeout.
    /// </summary>
    private async Task SendToProviderAsync(IAccountingProvider provider, EstimateExport export, AccountingInvoice invoice)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds));
        string idempotencyKey = export.Id.ToString();

        try
        {
            string externalReference = await provider.ExportInvoiceAsync(invoice, idempotencyKey, timeout.Token);
            export.MarkSucceeded(externalReference, _timeProvider.GetUtcNow());
            _logger.LogInformation(
                "Exported estimate {EstimateId} to {Provider} as {ExternalReference} (attempt {AttemptCount})",
                export.EstimateId,
                export.Provider,
                externalReference,
                export.AttemptCount);
        }
        catch (AccountingProviderException exception)
        {
            export.MarkFailed(exception.ErrorCode, exception.Message, _timeProvider.GetUtcNow());
            _logger.LogWarning(
                exception,
                "Export of estimate {EstimateId} to {Provider} failed with {ErrorCode} (attempt {AttemptCount})",
                export.EstimateId,
                export.Provider,
                exception.ErrorCode,
                export.AttemptCount);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            export.MarkFailed(
                ErrorCodes.ProviderTimeout,
                $"The accounting provider did not respond within {_options.ProviderTimeoutSeconds} seconds.",
                _timeProvider.GetUtcNow());
            _logger.LogWarning(
                "Export of estimate {EstimateId} to {Provider} timed out (attempt {AttemptCount})",
                export.EstimateId,
                export.Provider,
                export.AttemptCount);
        }

        await _repository.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<EstimateExportReadModel> ReturnConcurrentExportAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken)
    {
        EstimateExport? winner = await _repository.GetForUpdateAsync(tenantId, estimateId, provider, cancellationToken);
        if (winner is not null && winner.Status == ExportStatus.Succeeded)
        {
            return EstimateExportReadModel.FromExport(winner);
        }

        throw new ExportInProgressException(winner?.Id ?? Guid.Empty);
    }
}

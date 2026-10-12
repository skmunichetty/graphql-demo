using Accounting.Application;
using Accounting.Domain;
using GreenDonut.Data;

namespace Accounting.UnitTests;

internal static class TestData
{
    public static EstimateSnapshot Estimate(Guid id, string status = EstimateSnapshot.ApprovedStatus, string customerName = "Acme Builders")
    {
        var line = new EstimateLineSnapshot("Framing", 10m, 45.50m, 10m, 455.00m, 45.50m, 500.50m);
        return new EstimateSnapshot(id, status, "AUD", customerName, "accounts@acme.example", 455.00m, 45.50m, 500.50m, [line]);
    }
}

internal sealed class FakeEstimatesServiceClient : IEstimatesServiceClient
{
    public Dictionary<(string TenantId, Guid EstimateId), EstimateSnapshot> Estimates { get; } = [];

    public bool IsUnavailable { get; set; }

    public int CallCount { get; private set; }

    public Task<EstimateSnapshot?> GetEstimateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        CallCount++;
        if (IsUnavailable)
        {
            throw new EstimatesServiceUnavailableException("Simulated outage.");
        }

        Estimates.TryGetValue((tenantId, estimateId), out EstimateSnapshot? estimate);
        return Task.FromResult(estimate);
    }
}

/// <summary>A provider whose behaviour each test controls.</summary>
internal sealed class ScriptedAccountingProvider : IAccountingProvider
{
    public Queue<Func<string>> Responses { get; } = new();

    public List<string> IdempotencyKeys { get; } = [];

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public AccountingProvider Provider => AccountingProvider.Mock;

    public async Task<string> ExportInvoiceAsync(AccountingInvoice invoice, string idempotencyKey, CancellationToken cancellationToken)
    {
        IdempotencyKeys.Add(idempotencyKey);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        Func<string> next = Responses.Count > 0 ? Responses.Dequeue() : () => "REF-DEFAULT";
        return next();
    }
}

internal sealed class InMemoryExportRepository : IEstimateExportRepository
{
    public List<EstimateExport> Exports { get; } = [];

    public int SaveCount { get; private set; }

    public Task<EstimateExport?> GetForUpdateAsync(string tenantId, Guid estimateId, AccountingProvider provider, CancellationToken cancellationToken)
    {
        EstimateExport? export = Exports.FirstOrDefault(item =>
            item.TenantId == tenantId && item.EstimateId == estimateId && item.Provider == provider);
        return Task.FromResult(export);
    }

    public Task<bool> TryInsertAsync(EstimateExport export, CancellationToken cancellationToken)
    {
        bool exists = Exports.Any(item =>
            item.TenantId == export.TenantId && item.EstimateId == export.EstimateId && item.Provider == export.Provider);
        if (exists)
        {
            return Task.FromResult(false);
        }

        Exports.Add(export);
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class UnusedReadStore : IEstimateExportReadStore
{
    public Task<EstimateExportReadModel?> GetByEstimateAsync(string tenantId, Guid estimateId, AccountingProvider provider, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<EstimateExportReadModel?> GetByIdAsync(string tenantId, Guid exportId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<Page<EstimateExportReadModel>> ListAsync(string tenantId, PagingArguments pagingArguments, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}

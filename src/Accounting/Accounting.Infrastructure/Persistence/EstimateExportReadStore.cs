using Accounting.Application;
using Accounting.Domain;
using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// Read-side queries with a short-lived DbContext per call, so parallel GraphQL fields are safe.
/// Every query filters by tenant.
/// </summary>
public sealed class EstimateExportReadStore : IEstimateExportReadStore
{
    private readonly IDbContextFactory<AccountingDbContext> _dbContextFactory;

    public EstimateExportReadStore(IDbContextFactory<AccountingDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<EstimateExportReadModel?> GetByEstimateAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken)
    {
        await using AccountingDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await Project(dbContext.EstimateExports.AsNoTracking()
                .Where(export => export.TenantId == tenantId && export.EstimateId == estimateId && export.Provider == provider))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EstimateExportReadModel?> GetByIdAsync(string tenantId, Guid exportId, CancellationToken cancellationToken)
    {
        await using AccountingDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await Project(dbContext.EstimateExports.AsNoTracking()
                .Where(export => export.TenantId == tenantId && export.Id == exportId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Page<EstimateExportReadModel>> ListAsync(
        string tenantId,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken)
    {
        await using AccountingDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Keyset pagination, newest first; Id makes the ordering unique.
        IQueryable<EstimateExportReadModel> query = Project(dbContext.EstimateExports.AsNoTracking()
                .Where(export => export.TenantId == tenantId))
            .OrderByDescending(export => export.CreatedAt)
            .ThenByDescending(export => export.Id);

        return await query.ToPageAsync(pagingArguments, cancellationToken);
    }

    private static IQueryable<EstimateExportReadModel> Project(IQueryable<EstimateExport> exports)
    {
        return exports.Select(export => new EstimateExportReadModel
        {
            Id = export.Id,
            EstimateId = export.EstimateId,
            Provider = export.Provider,
            Status = export.Status,
            ExternalReference = export.ExternalReference,
            CurrencyCode = export.CurrencyCode,
            ExportedTotal = export.ExportedTotal,
            AttemptCount = export.AttemptCount,
            LastErrorCode = export.LastErrorCode,
            LastErrorMessage = export.LastErrorMessage,
            CreatedAt = export.CreatedAt,
            UpdatedAt = export.UpdatedAt,
            CompletedAt = export.CompletedAt,
        });
    }
}

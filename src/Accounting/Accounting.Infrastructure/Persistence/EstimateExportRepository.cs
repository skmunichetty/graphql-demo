using Accounting.Application;
using Accounting.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Infrastructure.Persistence;

/// <summary>Write-side repository using the request-scoped DbContext (mutations run one at a time).</summary>
public sealed class EstimateExportRepository : IEstimateExportRepository
{
    private readonly AccountingDbContext _dbContext;

    public EstimateExportRepository(AccountingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EstimateExport?> GetForUpdateAsync(
        string tenantId,
        Guid estimateId,
        AccountingProvider provider,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EstimateExports
            .Where(export => export.TenantId == tenantId && export.EstimateId == estimateId && export.Provider == provider)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryInsertAsync(EstimateExport export, CancellationToken cancellationToken)
    {
        _dbContext.EstimateExports.Add(export);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateExport(exception))
        {
            // Another request inserted the same export a moment earlier. Stop tracking our copy so
            // the caller can load the winning record with this same context.
            _dbContext.Entry(export).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDuplicateExport(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == AccountingDbContext.UniqueExportIndexName;
    }
}

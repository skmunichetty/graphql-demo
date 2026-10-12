using Estimates.Application;
using Estimates.Domain;
using Microsoft.EntityFrameworkCore;

namespace Estimates.Infrastructure.Persistence;

/// <summary>
/// Write-side repository. It uses the request-scoped <see cref="EstimatesDbContext"/> so that
/// the aggregate loaded at the start of a mutation is change-tracked until it is saved.
/// GraphQL mutations run one after another, so this scoped context is never used concurrently.
/// </summary>
public sealed class EstimateRepository : IEstimateRepository
{
    private readonly EstimatesDbContext _dbContext;

    public EstimateRepository(EstimatesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Estimate?> GetForUpdateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        // Tenant is part of the WHERE clause: an estimate in another tenant is simply not found.
        return await _dbContext.Estimates
            .Include(estimate => estimate.LineItems)
            .Where(estimate => estimate.TenantId == tenantId && estimate.Id == estimateId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(Estimate estimate)
    {
        _dbContext.Estimates.Add(estimate);
    }

    public async Task SaveChangesAsync(Estimate estimate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Another request saved a newer version between our load and our save.
            // Version - 1 is the version this request originally loaded.
            throw new ConcurrencyConflictException(estimate.Id, estimate.Version - 1, currentVersion: null, exception);
        }
    }
}

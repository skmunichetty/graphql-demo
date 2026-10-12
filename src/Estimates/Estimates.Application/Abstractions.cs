using Estimates.Domain;
using GreenDonut.Data;

namespace Estimates.Application;

/// <summary>
/// Write-side persistence for the estimate aggregate. This is a specific repository for one
/// aggregate, not a generic repository: it only has the operations the use cases need.
/// The interface lives here (Application) and is implemented in Infrastructure, so the
/// application does not depend on EF Core (dependency inversion).
/// </summary>
public interface IEstimateRepository
{
    /// <summary>Loads the full aggregate, including line items, only if it belongs to the tenant.</summary>
    Task<Estimate?> GetForUpdateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken);

    void Add(Estimate estimate);

    /// <summary>
    /// Saves all changes. Throws <see cref="ConcurrencyConflictException"/> if another request
    /// changed the estimate after it was loaded.
    /// </summary>
    Task SaveChangesAsync(Estimate estimate, CancellationToken cancellationToken);
}

/// <summary>
/// Read-side queries. Every method takes the tenant explicitly and filters by it in SQL: this is
/// where tenant isolation is enforced for reads, not just at the GraphQL endpoint.
/// Implementations must be safe to call concurrently, because GraphQL may resolve sibling
/// fields in parallel.
/// </summary>
public interface IEstimateReadStore
{
    Task<EstimateReadModel?> GetByIdAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken);

    /// <summary>Cursor-paged list, newest first. The page size is bounded by the API layer.</summary>
    Task<Page<EstimateReadModel>> ListAsync(
        string tenantId,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken);

    /// <summary>Loads line items for many estimates in one query (used by a DataLoader to avoid N+1 queries).</summary>
    Task<IReadOnlyDictionary<Guid, LineItemReadModel[]>> GetLineItemsByEstimateIdsAsync(
        string tenantId,
        IReadOnlyCollection<Guid> estimateIds,
        CancellationToken cancellationToken);
}

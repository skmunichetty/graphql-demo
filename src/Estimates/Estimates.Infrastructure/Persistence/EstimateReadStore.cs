using Estimates.Application;
using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;

namespace Estimates.Infrastructure.Persistence;

/// <summary>
/// Read-side queries. Each method creates its own short-lived DbContext from a factory because
/// Hot Chocolate may resolve sibling query fields in parallel, and a DbContext is not thread-safe.
/// Queries are no-tracking and project straight into read models, so only needed columns are read.
/// </summary>
public sealed class EstimateReadStore : IEstimateReadStore
{
    private readonly IDbContextFactory<EstimatesDbContext> _dbContextFactory;

    public EstimateReadStore(IDbContextFactory<EstimatesDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<EstimateReadModel?> GetByIdAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        await using EstimatesDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await ProjectToReadModel(dbContext.Estimates.AsNoTracking())
            .Where(estimate => estimate.TenantId == tenantId && estimate.Id == estimateId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Page<EstimateReadModel>> ListAsync(
        string tenantId,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken)
    {
        await using EstimatesDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Keyset (cursor) pagination: the cursor encodes (CreatedAt, Id) of the last item, and the next
        // page is "WHERE (created_at, id) < cursor". Unlike OFFSET, this stays fast on large tables
        // and does not skip or repeat rows when new estimates are inserted between requests.
        // Id is the tie-breaker that makes the ordering unique, which cursor paging requires.
        IQueryable<EstimateReadModel> query = ProjectToReadModel(dbContext.Estimates.AsNoTracking())
            .Where(estimate => estimate.TenantId == tenantId)
            .OrderByDescending(estimate => estimate.CreatedAt)
            .ThenByDescending(estimate => estimate.Id);

        return await query.ToPageAsync(pagingArguments, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, LineItemReadModel[]>> GetLineItemsByEstimateIdsAsync(
        string tenantId,
        IReadOnlyCollection<Guid> estimateIds,
        CancellationToken cancellationToken)
    {
        await using EstimatesDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One query for all requested estimates: "WHERE tenant_id = @t AND estimate_id = ANY(@ids)".
        List<LineItemReadModel> lineItems = await dbContext.LineItems
            .AsNoTracking()
            .Where(lineItem => lineItem.TenantId == tenantId && estimateIds.Contains(lineItem.EstimateId))
            .OrderBy(lineItem => lineItem.EstimateId)
            .ThenBy(lineItem => lineItem.Position)
            .Select(lineItem => new LineItemReadModel
            {
                Id = lineItem.Id,
                EstimateId = lineItem.EstimateId,
                Position = lineItem.Position,
                Description = lineItem.Description,
                Quantity = lineItem.Quantity,
                UnitPrice = lineItem.UnitPrice,
                TaxRatePercent = lineItem.TaxRatePercent,
                Subtotal = lineItem.Subtotal,
                TaxAmount = lineItem.TaxAmount,
                Total = lineItem.Total,
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, LineItemReadModel[]>();
        foreach (IGrouping<Guid, LineItemReadModel> group in lineItems.GroupBy(lineItem => lineItem.EstimateId))
        {
            result.Add(group.Key, group.ToArray());
        }

        return result;
    }

    private static IQueryable<EstimateReadModel> ProjectToReadModel(IQueryable<Domain.Estimate> estimates)
    {
        return estimates.Select(estimate => new EstimateReadModel
        {
            Id = estimate.Id,
            TenantId = estimate.TenantId,
            Customer = new CustomerReadModel
            {
                Name = estimate.Customer.Name,
                Email = estimate.Customer.Email,
                Phone = estimate.Customer.Phone,
            },
            CurrencyCode = estimate.CurrencyCode,
            Status = estimate.Status,
            Subtotal = estimate.Subtotal,
            TaxTotal = estimate.TaxTotal,
            Total = estimate.Total,
            Version = estimate.Version,
            CreatedAt = estimate.CreatedAt,
            UpdatedAt = estimate.UpdatedAt,
            ApprovedAt = estimate.ApprovedAt,
        });
    }
}

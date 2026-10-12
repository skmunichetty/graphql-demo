using Estimates.Application;

namespace Estimates.Api.GraphQL;

/// <summary>
/// The key includes the tenant so that the batch query can filter by tenant as well as by estimate.
/// The parent estimate was already loaded with a tenant filter, so this is defence in depth.
/// </summary>
public readonly record struct EstimateLineItemsKey(string TenantId, Guid EstimateId);

/// <summary>
/// Batches line-item lookups made while resolving one GraphQL request. A new instance is created
/// per request (scoped), so cached results never leak between requests or tenants.
/// </summary>
public sealed class LineItemsByEstimateDataLoader : BatchDataLoader<EstimateLineItemsKey, LineItemReadModel[]>
{
    private readonly IEstimateReadStore _readStore;

    public LineItemsByEstimateDataLoader(
        IEstimateReadStore readStore,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _readStore = readStore;
    }

    protected override async Task<IReadOnlyDictionary<EstimateLineItemsKey, LineItemReadModel[]>> LoadBatchAsync(
        IReadOnlyList<EstimateLineItemsKey> keys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<EstimateLineItemsKey, LineItemReadModel[]>();

        // Normally every key in a request has the same tenant, so this loop runs one query.
        foreach (IGrouping<string, EstimateLineItemsKey> tenantGroup in keys.GroupBy(key => key.TenantId))
        {
            Guid[] estimateIds = tenantGroup.Select(key => key.EstimateId).Distinct().ToArray();
            IReadOnlyDictionary<Guid, LineItemReadModel[]> lineItemsByEstimate =
                await _readStore.GetLineItemsByEstimateIdsAsync(tenantGroup.Key, estimateIds, cancellationToken);

            foreach (EstimateLineItemsKey key in tenantGroup)
            {
                if (lineItemsByEstimate.TryGetValue(key.EstimateId, out LineItemReadModel[]? lineItems))
                {
                    result[key] = lineItems;
                }
                else
                {
                    result[key] = [];
                }
            }
        }

        return result;
    }
}

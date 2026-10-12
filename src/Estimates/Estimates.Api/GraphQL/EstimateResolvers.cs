using Estimates.Application;

namespace Estimates.Api.GraphQL;

/// <summary>Field resolvers for the Estimate type.</summary>
public sealed class EstimateResolvers
{
    /// <summary>
    /// Resolves line items through a DataLoader. Without it, a list of 50 estimates that each ask for
    /// lineItems would run 1 + 50 queries (the N+1 problem). The DataLoader collects all 50 keys and
    /// loads them in a single query.
    /// </summary>
    public async Task<IReadOnlyList<LineItemReadModel>> GetLineItemsAsync(
        [Parent] EstimateReadModel estimate,
        LineItemsByEstimateDataLoader lineItemsLoader,
        CancellationToken cancellationToken)
    {
        var key = new EstimateLineItemsKey(estimate.TenantId, estimate.Id);
        LineItemReadModel[]? lineItems = await lineItemsLoader.LoadAsync(key, cancellationToken);
        if (lineItems is null)
        {
            return [];
        }

        return lineItems;
    }
}

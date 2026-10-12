using System.Security.Claims;
using Estimates.Api.Security;
using Estimates.Application;
using GreenDonut.Data;
using HotChocolate.Authorization;
using HotChocolate.Types.Pagination;
using ServiceDefaults.Security;

namespace Estimates.Api.GraphQL;

/// <summary>
/// Query root. Resolvers are deliberately thin: read the tenant from the token, call the
/// application layer, return the result. No business rules live here.
/// </summary>
public sealed class Query
{
    [Authorize(Policy = Policies.EstimatesRead)]
    [GraphQLDescription("Returns the estimate, or null if it does not exist in your tenant.")]
    public async Task<EstimateReadModel?> GetEstimateAsync(
        Guid id,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] IEstimateReadStore readStore,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        return await readStore.GetByIdAsync(tenantId, id, cancellationToken);
    }

    [Authorize(Policy = Policies.EstimatesRead)]
    [GraphQLDescription("Estimates in your tenant, newest first. Cursor-paginated: at most 50 per page, 20 by default.")]
    [UsePaging(MaxPageSize = 50, DefaultPageSize = 20, IncludeTotalCount = false)]
    public async Task<Connection<EstimateReadModel>> GetEstimatesAsync(
        PagingArguments pagingArguments,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] IEstimateReadStore readStore,
        CancellationToken cancellationToken)
    {
        // UsePaging validates first/last against MaxPageSize before this runs and passes the
        // client's first/after/last/before in pagingArguments. The database does the paging.
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        Page<EstimateReadModel> page = await readStore.ListAsync(tenantId, pagingArguments, cancellationToken);
        return page.ToConnection();
    }

    /// <summary>
    /// Service-to-service query. A service token (client credentials) represents the Accounting
    /// service, not a user, so it has no tenant claim. The calling service states the tenant it is
    /// acting for; this is only trusted because the policy requires the integration scope, which the
    /// identity provider grants to the Accounting service alone. See docs/security.md.
    /// </summary>
    [Authorize(Policy = Policies.EstimatesIntegrationRead)]
    [GraphQLDescription("Service-to-service only (scope estimates:integration.read). Returns the estimate for the named tenant, or null.")]
    public async Task<EstimateReadModel?> GetEstimateForIntegrationAsync(
        string tenantId,
        Guid id,
        [Service] IEstimateReadStore readStore,
        CancellationToken cancellationToken)
    {
        return await readStore.GetByIdAsync(tenantId, id, cancellationToken);
    }
}

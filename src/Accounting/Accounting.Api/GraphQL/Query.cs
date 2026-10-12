using System.Security.Claims;
using Accounting.Api.Security;
using Accounting.Application;
using Accounting.Domain;
using GreenDonut.Data;
using HotChocolate.Authorization;
using HotChocolate.Types.Pagination;
using ServiceDefaults.Security;

namespace Accounting.Api.GraphQL;

/// <summary>Query root: export status lookups. Thin resolvers; tenant always from the token.</summary>
public sealed class Query
{
    [Authorize(Policy = Policies.AccountingRead)]
    [GraphQLDescription("The export of an estimate to a provider, or null if it has never been exported.")]
    public async Task<EstimateExportReadModel?> GetEstimateExportAsync(
        Guid estimateId,
        [GraphQLDescription("Optional. Defaults to MOCK.")] AccountingProvider? provider,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] IEstimateExportReadStore readStore,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        return await readStore.GetByEstimateAsync(tenantId, estimateId, provider ?? AccountingProvider.Mock, cancellationToken);
    }

    [Authorize(Policy = Policies.AccountingRead)]
    [GraphQLDescription("An export by its ID, or null if it does not exist in your tenant.")]
    public async Task<EstimateExportReadModel?> GetEstimateExportByIdAsync(
        Guid id,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] IEstimateExportReadStore readStore,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        return await readStore.GetByIdAsync(tenantId, id, cancellationToken);
    }

    [Authorize(Policy = Policies.AccountingRead)]
    [GraphQLDescription("Exports in your tenant, newest first. Cursor-paginated: at most 50 per page, 20 by default.")]
    [UsePaging(MaxPageSize = 50, DefaultPageSize = 20, IncludeTotalCount = false)]
    public async Task<Connection<EstimateExportReadModel>> GetEstimateExportsAsync(
        PagingArguments pagingArguments,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] IEstimateExportReadStore readStore,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        Page<EstimateExportReadModel> page = await readStore.ListAsync(tenantId, pagingArguments, cancellationToken);
        return page.ToConnection();
    }
}

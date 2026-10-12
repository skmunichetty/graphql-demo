using System.Security.Claims;
using Accounting.Api.Security;
using Accounting.Application;
using Accounting.Domain;
using HotChocolate.Authorization;
using ServiceDefaults.Security;

namespace Accounting.Api.GraphQL;

/// <summary>Mutation root. See EstimateExportService for the idempotency rules.</summary>
public sealed class Mutation
{
    [Authorize(Policy = Policies.AccountingExport)]
    [GraphQLDescription(
        "Exports an approved estimate to an accounting provider. Idempotent: repeating the request returns the " +
        "existing export instead of creating a duplicate. If the provider fails, the export is returned with " +
        "status FAILED and lastErrorCode set; call this mutation again to retry.")]
    [Error(typeof(EstimateNotFoundError))]
    [Error(typeof(EstimateNotApprovedError))]
    [Error(typeof(ExportInProgressError))]
    [Error(typeof(EstimatesServiceUnavailableError))]
    [Error(typeof(UnsupportedProviderError))]
    [UseMutationConvention(PayloadFieldName = "export")]
    public async Task<EstimateExportReadModel> ExportEstimateAsync(
        Guid estimateId,
        [GraphQLDescription("Optional. Defaults to MOCK.")] AccountingProvider? provider,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateExportService exportService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        return await exportService.ExportAsync(tenantId, estimateId, provider ?? AccountingProvider.Mock, cancellationToken);
    }
}

using System.Security.Claims;
using Estimates.Api.GraphQL.Errors;
using Estimates.Api.Security;
using Estimates.Application;
using HotChocolate.Authorization;
using ServiceDefaults.Security;

namespace Estimates.Api.GraphQL;

/// <summary>
/// Mutation root. Mutation conventions turn each method into:
///   fieldName(input: FieldNameInput!): FieldNamePayload!
/// where the payload has the result ("estimate") and a typed "errors" list for the listed exceptions.
/// Every change to an existing estimate requires expectedVersion (optimistic concurrency).
/// </summary>
public sealed class Mutation
{
    [Authorize(Policy = Policies.EstimatesWrite)]
    [GraphQLDescription("Creates a draft estimate in your tenant.")]
    [Error(typeof(ValidationError))]
    [UseMutationConvention(PayloadFieldName = "estimate")]
    public async Task<EstimateReadModel> CreateEstimateAsync(
        string customerName,
        string? customerEmail,
        string? customerPhone,
        [GraphQLDescription("ISO 4217 code: AUD, CAD, EUR, GBP, NZD or USD.")] string currencyCode,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateService estimateService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        var command = new CreateEstimateCommand(customerName, customerEmail, customerPhone, currencyCode);
        return await estimateService.CreateDraftAsync(tenantId, command, cancellationToken);
    }

    [Authorize(Policy = Policies.EstimatesWrite)]
    [GraphQLDescription("Adds a line item to a draft estimate.")]
    [Error(typeof(ValidationError))]
    [Error(typeof(EstimateNotFoundError))]
    [Error(typeof(InvalidEstimateStateError))]
    [Error(typeof(ConcurrencyConflictError))]
    [UseMutationConvention(PayloadFieldName = "estimate")]
    public async Task<EstimateReadModel> AddLineItemAsync(
        Guid estimateId,
        int expectedVersion,
        string description,
        decimal quantity,
        decimal unitPrice,
        [GraphQLDescription("Percent, for example 10 for 10%. Omit to use the server's demo default.")] decimal? taxRatePercent,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateService estimateService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        var command = new AddLineItemCommand(estimateId, expectedVersion, description, quantity, unitPrice, taxRatePercent);
        return await estimateService.AddLineItemAsync(tenantId, command, cancellationToken);
    }

    [Authorize(Policy = Policies.EstimatesWrite)]
    [GraphQLDescription("Replaces the details of a line item on a draft estimate.")]
    [Error(typeof(ValidationError))]
    [Error(typeof(EstimateNotFoundError))]
    [Error(typeof(LineItemNotFoundError))]
    [Error(typeof(InvalidEstimateStateError))]
    [Error(typeof(ConcurrencyConflictError))]
    [UseMutationConvention(PayloadFieldName = "estimate")]
    public async Task<EstimateReadModel> UpdateLineItemAsync(
        Guid estimateId,
        Guid lineItemId,
        int expectedVersion,
        string description,
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateService estimateService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        var command = new UpdateLineItemCommand(estimateId, lineItemId, expectedVersion, description, quantity, unitPrice, taxRatePercent);
        return await estimateService.UpdateLineItemAsync(tenantId, command, cancellationToken);
    }

    [Authorize(Policy = Policies.EstimatesWrite)]
    [GraphQLDescription("Removes a line item from a draft estimate.")]
    [Error(typeof(EstimateNotFoundError))]
    [Error(typeof(LineItemNotFoundError))]
    [Error(typeof(InvalidEstimateStateError))]
    [Error(typeof(ConcurrencyConflictError))]
    [UseMutationConvention(PayloadFieldName = "estimate")]
    public async Task<EstimateReadModel> RemoveLineItemAsync(
        Guid estimateId,
        Guid lineItemId,
        int expectedVersion,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateService estimateService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        var command = new RemoveLineItemCommand(estimateId, lineItemId, expectedVersion);
        return await estimateService.RemoveLineItemAsync(tenantId, command, cancellationToken);
    }

    [Authorize(Policy = Policies.EstimatesWrite)]
    [GraphQLDescription("Approves a draft estimate. Approved estimates cannot be changed.")]
    [Error(typeof(EstimateNotFoundError))]
    [Error(typeof(InvalidEstimateStateError))]
    [Error(typeof(ConcurrencyConflictError))]
    [UseMutationConvention(PayloadFieldName = "estimate")]
    public async Task<EstimateReadModel> ApproveEstimateAsync(
        Guid estimateId,
        int expectedVersion,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] EstimateService estimateService,
        CancellationToken cancellationToken)
    {
        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        var command = new ApproveEstimateCommand(estimateId, expectedVersion);
        return await estimateService.ApproveAsync(tenantId, command, cancellationToken);
    }
}

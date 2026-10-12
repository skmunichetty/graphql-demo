using System.Runtime.CompilerServices;
using System.Security.Claims;
using Estimates.Api.Security;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using ServiceDefaults.Security;

namespace Estimates.Api.GraphQL;

/// <summary>
/// Subscription root. Events are delivered over Server-Sent Events on /graphql (Accept:
/// text/event-stream) or over WebSocket on /graphql/ws. Events are kept in memory, so a subscriber
/// only sees changes made through the same API instance (see docs/architecture.md).
/// </summary>
public sealed class Subscription
{
    [Authorize(Policy = Policies.EstimatesRead)]
    [GraphQLDescription("Changes to estimates in your tenant, as they are saved. Pass estimateId to follow one estimate.")]
    [Subscribe(With = nameof(SubscribeToEstimateChangesAsync))]
    public EstimateChangedEvent EstimateChanged(
        [GraphQLDescription("Only report changes to this estimate.")] Guid? estimateId,
        [EventMessage] EstimateChangedEvent change)
    {
        return change;
    }

    public async IAsyncEnumerable<EstimateChangedEvent> SubscribeToEstimateChangesAsync(
        Guid? estimateId,
        ClaimsPrincipal user,
        [Service] TenantClaimReader tenantClaimReader,
        [Service] ITopicEventReceiver eventReceiver,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The stream is opened before field authorization runs on each event, so check here too:
        // a caller without the read scope must not even open a stream.
        if (!user.HasScope(Scopes.EstimatesRead))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The current user is not authorized to access this resource.")
                .SetCode(ErrorCodes.Authentication.NotAuthorized)
                .Build());
        }

        string tenantId = tenantClaimReader.GetRequiredTenantId(user);
        await using ISourceStream<EstimateChangedEvent> stream =
            await eventReceiver.SubscribeAsync<EstimateChangedEvent>(EstimateTopics.ForTenant(tenantId), cancellationToken);

        await foreach (EstimateChangedEvent change in stream.ReadEventsAsync().WithCancellation(cancellationToken))
        {
            if (estimateId is null || change.Estimate.Id == estimateId)
            {
                yield return change;
            }
        }
    }
}

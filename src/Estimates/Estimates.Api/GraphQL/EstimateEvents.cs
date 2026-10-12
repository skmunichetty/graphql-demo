using Estimates.Application;
using HotChocolate.Subscriptions;

namespace Estimates.Api.GraphQL;

public enum EstimateChangeKind
{
    Created,
    LineItemAdded,
    LineItemUpdated,
    LineItemRemoved,
    Approved,
}

/// <summary>Published after a mutation has been saved. Carries the estimate as it is now.</summary>
public sealed record EstimateChangedEvent(EstimateChangeKind Kind, EstimateReadModel Estimate);

/// <summary>
/// One topic per tenant. Subscribers derive the topic from their own token, so a client can only
/// ever receive events from its own tenant.
/// </summary>
public static class EstimateTopics
{
    public static string ForTenant(string tenantId) => $"estimates:{tenantId}";

    public static ValueTask PublishAsync(this ITopicEventSender sender, EstimateChangeKind kind, EstimateReadModel estimate)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(estimate);

        // The change is already committed, so publish even if the client has gone away.
        return sender.SendAsync(ForTenant(estimate.TenantId), new EstimateChangedEvent(kind, estimate), CancellationToken.None);
    }
}

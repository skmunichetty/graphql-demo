using Estimates.Api.GraphQL.Errors;
using Estimates.Api.GraphQL.Types;
using ServiceDefaults;
using ServiceDefaults.GraphQL;

namespace Estimates.Api.GraphQL;

public static class GraphQLSetup
{
    public static IServiceCollection AddEstimatesGraphQL(this IServiceCollection services, GraphQLSettings settings)
    {
        // DataLoaders are registered as scoped services: one instance (and one cache) per request.
        services.AddDataLoader<LineItemsByEstimateDataLoader>();

        services
            .AddGraphQLServer()
            .AddGraphQLServerDefaults(settings)
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddSubscriptionType<Subscription>()
            .AddType<EstimateType>()
            .AddType<CustomerType>()
            .AddType<LineItemType>()
            .AddType<EstimateStatusType>()
            .AddType<EstimateChangeType>()
            .AddType<EstimateChangeKindType>()
            .AddErrorInterfaceType<IUserError>()

            // In-process pub/sub: events reach subscribers connected to the same instance only.
            // With several replicas, switch to a shared provider (Redis or PostgreSQL).
            .AddInMemorySubscriptions()
            .AddSocketSessionInterceptor<WebSocketAuthenticationInterceptor>();

        return services;
    }
}

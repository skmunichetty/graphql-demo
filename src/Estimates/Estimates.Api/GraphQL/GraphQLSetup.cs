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
            .AddType<EstimateType>()
            .AddType<CustomerType>()
            .AddType<LineItemType>()
            .AddType<EstimateStatusType>()
            .AddErrorInterfaceType<IUserError>();

        return services;
    }
}

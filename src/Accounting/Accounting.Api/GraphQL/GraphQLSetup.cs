using ServiceDefaults;
using ServiceDefaults.GraphQL;

namespace Accounting.Api.GraphQL;

public static class GraphQLSetup
{
    public static IServiceCollection AddAccountingGraphQL(this IServiceCollection services, GraphQLSettings settings)
    {
        services
            .AddGraphQLServer()
            .AddGraphQLServerDefaults(settings)
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddType<EstimateExportType>()
            .AddType<AccountingProviderType>()
            .AddType<ExportStatusType>()
            .AddErrorInterfaceType<IUserError>();

        return services;
    }
}

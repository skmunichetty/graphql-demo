using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.GraphQL;

public static class GraphQLServerDefaults
{
    /// <summary>
    /// Applies the same protections and conventions to every service's GraphQL server.
    /// Each service then adds its own types, queries and mutations.
    /// </summary>
    public static IRequestExecutorBuilder AddGraphQLServerDefaults(this IRequestExecutorBuilder builder, GraphQLSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return builder
            .AddAuthorization()
            .AddMutationConventions(applyToAllMutations: true)
            .AddPagingArguments()
            .AddInstrumentation()

            // Only explicit [GraphQLDescription] text is published. Otherwise C# XML comments,
            // which are written for maintainers, would leak into the public schema.
            .ModifyOptions(options =>
            {
                options.UseXmlDocumentation = false;
            })

            // Error handling and visibility.
            // Hot Chocolate 16 builds the schema with its own service provider, so application
            // services used by schema-level components must be cross-registered explicitly.
            .AddErrorFilter<UnexpectedErrorFilter>()
            .AddApplicationService<ILoggerFactory>()
            .AddDiagnosticEventListener(serviceProvider => new GraphQLErrorLoggingListener(
                serviceProvider.GetRequiredService<ILoggerFactory>()))
            .ModifyRequestOptions(options =>
            {
                options.IncludeExceptionDetails = settings.IncludeExceptionDetails;

                // Stops runaway operations; the client receives an error instead of waiting forever.
                options.ExecutionTimeout = TimeSpan.FromSeconds(settings.ExecutionTimeoutSeconds);
            })

            // Request limits. Each limit rejects an operation before any resolver runs.
            // - Depth: stops deeply nested queries.
            // - Cost: stops operations that would do too much work or return too much data. Lists are
            //   estimated from the page size arguments, so bounded pagination keeps costs predictable.
            // - Parser: caps document size so huge queries are rejected cheaply.
            .AddMaxExecutionDepthRule(settings.MaxExecutionDepth, skipIntrospectionFields: true)
            .ModifyCostOptions(options =>
            {
                options.MaxFieldCost = settings.MaxFieldCost;
                options.MaxTypeCost = settings.MaxTypeCost;
                options.EnforceCostLimits = true;
            })
            .ModifyParserOptions(options =>
            {
                options.MaxAllowedFields = 256;
                options.MaxAllowedNodes = 5_000;
            })
            .DisableIntrospection(!settings.EnableIntrospection)
            .ModifyServerOptions(options =>
            {
                // GET requests are disabled: they can be triggered cross-site and end up in URL logs.
                options.EnableGetRequests = false;
                options.EnableMultipartRequests = false;
                options.EnableSchemaRequests = settings.EnableDeveloperTools;
                options.Tool.Enable = false;
            });
    }
}

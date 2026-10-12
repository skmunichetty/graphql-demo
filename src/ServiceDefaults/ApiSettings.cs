using System.ComponentModel.DataAnnotations;

namespace ServiceDefaults;

/// <summary>GraphQL server behaviour that differs between environments (section "GraphQL").</summary>
public sealed class GraphQLSettings
{
    public const string SectionName = "GraphQL";

    /// <summary>Serves the Nitro IDE at /graphql/ui and the SDL at /graphql/schema.graphql. Development only.</summary>
    public bool EnableDeveloperTools { get; set; }

    /// <summary>
    /// Allows introspection queries. Useful during development and for trusted consumers generating
    /// clients; disabled by default in production to reduce the information exposed to attackers.
    /// Publish the schema file (schema/*.graphql) to consumers instead.
    /// </summary>
    public bool EnableIntrospection { get; set; }

    /// <summary>Adds exception messages and stack traces to GraphQL errors. Never enable in production.</summary>
    public bool IncludeExceptionDetails { get; set; }

    [Range(1, 30)]
    public int MaxExecutionDepth { get; set; } = 8;

    [Range(1, 120)]
    public int ExecutionTimeoutSeconds { get; set; } = 10;

    [Range(1, 100_000)]
    public double MaxFieldCost { get; set; } = 1_000;

    [Range(1, 100_000)]
    public double MaxTypeCost { get; set; } = 5_000;
}

/// <summary>Cross-origin settings (section "Cors"). Empty means no browser origin is allowed.</summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "GraphQLClients";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Per-client request rate limit (section "RateLimiting").</summary>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";
    public const string PolicyName = "per-client";

    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 10;
}

/// <summary>Hosting behaviour (section "Hosting").</summary>
public sealed class HostingSettings
{
    public const string SectionName = "Hosting";

    /// <summary>Enables HSTS and HTTPS redirection. Only disable for local HTTP development.</summary>
    public bool EnforceHttps { get; set; } = true;

    /// <summary>
    /// Applies EF Core migrations when the service starts. Convenient locally; in production run
    /// migrations as a separate, deliberate deployment step instead (see docs/deployment.md).
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

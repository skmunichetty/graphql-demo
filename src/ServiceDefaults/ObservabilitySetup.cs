using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ServiceDefaults.GraphQL;

namespace ServiceDefaults;

public static class ObservabilitySetup
{
    /// <summary>
    /// OpenTelemetry traces, metrics and logs. Data is exported over OTLP only when
    /// OTEL_EXPORTER_OTLP_ENDPOINT is configured (for example to the Aspire dashboard in
    /// docker-compose, or to a vendor collector in production). Without it, instrumentation still
    /// runs and trace IDs still appear in logs.
    /// </summary>
    public static WebApplicationBuilder AddServiceObservability(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Adds TraceId and SpanId to every log scope, so logs can be joined to traces.
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId;
        });

        OpenTelemetryBuilder openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes run every few seconds and would drown out real traffic.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                });
                tracing.AddHttpClientInstrumentation();
                tracing.AddHotChocolateInstrumentation();
                tracing.AddNpgsql();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddMeter(GraphQLErrorLoggingListener.MeterName);
            })
            .WithLogging();

        string? otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }
}

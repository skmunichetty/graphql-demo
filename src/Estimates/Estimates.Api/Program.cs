using Estimates.Api.GraphQL;
using Estimates.Api.Security;
using Estimates.Application;
using Estimates.Infrastructure;
using Estimates.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ServiceDefaults;
using ServiceDefaults.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---- Configuration (validated now, so a bad setting stops startup with a clear message) ----
GraphQLSettings graphQLSettings = builder.Configuration.GetValidatedSettings<GraphQLSettings>(GraphQLSettings.SectionName);
CorsSettings corsSettings = builder.Configuration.GetValidatedSettings<CorsSettings>(CorsSettings.SectionName);
RateLimitingSettings rateLimitingSettings = builder.Configuration.GetValidatedSettings<RateLimitingSettings>(RateLimitingSettings.SectionName);
HostingSettings hostingSettings = builder.Configuration.GetValidatedSettings<HostingSettings>(HostingSettings.SectionName);
string connectionString = builder.Configuration.GetRequiredConnectionString("EstimatesDb");

// GraphQL requests are small JSON documents; there are no file uploads.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 256 * 1024;
});

// ---- Services ----
builder.AddServiceObservability("estimates-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<PricingOptions>()
    .Bind(builder.Configuration.GetSection(PricingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddEstimatesInfrastructure(connectionString);
builder.Services.AddScoped<EstimateService>();

builder.Services.AddJwtBearerAuthentication(builder.Configuration);
builder.Services.AddEstimatesAuthorization(builder.Configuration);
builder.Services.AddEstimatesGraphQL(graphQLSettings);

builder.Services.AddApiCors(corsSettings);
builder.Services.AddPerClientRateLimiting(rateLimitingSettings);
builder.Services.AddHealthChecks().AddDbContextCheck<EstimatesDbContext>("database", tags: ["ready"]);

// ---- HTTP pipeline ----
WebApplication app = builder.Build();

if (hostingSettings.ApplyMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    EstimatesDbContext dbContext = scope.ServiceProvider.GetRequiredService<EstimatesDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();

if (hostingSettings.EnforceHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseWebSockets();

app.MapHealthEndpoints();

// The GraphQL endpoint requires a valid access token at the HTTP level (401 without one).
// Field-level [Authorize] policies then check scopes and tenant claims (GraphQL errors).
// Subscriptions work here too, as Server-Sent Events (Accept: text/event-stream).
app.MapGraphQLHttp("/graphql")
    .RequireAuthorization()
    .RequireCors(CorsSettings.PolicyName)
    .RequireRateLimiting(RateLimitingSettings.PolicyName);

// Subscriptions over WebSocket (graphql-transport-ws). Browsers cannot send headers on a WebSocket,
// so the upgrade itself is anonymous and the token is checked from the connection_init message
// (WebSocketAuthenticationInterceptor) before any operation runs.
app.MapGraphQLWebSocket("/graphql/ws")
    .RequireRateLimiting(RateLimitingSettings.PolicyName);

if (graphQLSettings.EnableDeveloperTools)
{
    // Development tooling is served on separate, anonymous routes. The IDE itself holds no data;
    // every query it sends still goes to /graphql and needs a token.
    app.MapNitroApp("/graphql/ui").WithOptions(options =>
    {
        options.GraphQLEndpoint = "/graphql";
    });
    app.MapGraphQLSchema("/graphql/schema.graphql");
}

await app.RunAsync();

/// <summary>Exposed so integration tests can start the application with WebApplicationFactory.</summary>
public partial class Program
{
}

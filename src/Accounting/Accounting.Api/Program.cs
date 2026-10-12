using Accounting.Api.GraphQL;
using Accounting.Api.Security;
using Accounting.Application;
using Accounting.Infrastructure;
using Accounting.Infrastructure.EstimatesService;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using ServiceDefaults;
using ServiceDefaults.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---- Configuration (validated now, so a bad setting stops startup with a clear message) ----
GraphQLSettings graphQLSettings = builder.Configuration.GetValidatedSettings<GraphQLSettings>(GraphQLSettings.SectionName);
CorsSettings corsSettings = builder.Configuration.GetValidatedSettings<CorsSettings>(CorsSettings.SectionName);
RateLimitingSettings rateLimitingSettings = builder.Configuration.GetValidatedSettings<RateLimitingSettings>(RateLimitingSettings.SectionName);
HostingSettings hostingSettings = builder.Configuration.GetValidatedSettings<HostingSettings>(HostingSettings.SectionName);
string connectionString = builder.Configuration.GetRequiredConnectionString("AccountingDb");

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 64 * 1024;
});

// Options used after startup are validated when the host starts (ValidateOnStart).
builder.Services.AddOptions<ExportOptions>()
    .Bind(builder.Configuration.GetSection(ExportOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<EstimatesServiceOptions>()
    .Bind(builder.Configuration.GetSection(EstimatesServiceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<MockAccountingProviderOptions>()
    .Bind(builder.Configuration.GetSection(MockAccountingProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---- Services ----
builder.AddServiceObservability("accounting-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationIdPropagationHandler>();

builder.Services.AddAccountingInfrastructure(connectionString);
builder.Services.AddEstimatesServiceClient()
    .AddHttpMessageHandler<CorrelationIdPropagationHandler>();
builder.Services.AddScoped<EstimateExportService>();

builder.Services.AddJwtBearerAuthentication(builder.Configuration);
builder.Services.AddAccountingAuthorization(builder.Configuration);
builder.Services.AddAccountingGraphQL(graphQLSettings);

builder.Services.AddApiCors(corsSettings);
builder.Services.AddPerClientRateLimiting(rateLimitingSettings);

// Readiness checks only this service's own database. The Estimates service being down does not
// make this service "not ready": status queries still work, and exports report a clear error.
builder.Services.AddHealthChecks().AddDbContextCheck<AccountingDbContext>("database", tags: ["ready"]);

// ---- HTTP pipeline ----
WebApplication app = builder.Build();

if (hostingSettings.ApplyMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    AccountingDbContext dbContext = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
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

app.MapHealthEndpoints();

app.MapGraphQLHttp("/graphql")
    .RequireAuthorization()
    .RequireCors(CorsSettings.PolicyName)
    .RequireRateLimiting(RateLimitingSettings.PolicyName);

if (graphQLSettings.EnableDeveloperTools)
{
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

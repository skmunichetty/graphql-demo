using System.Collections.Concurrent;
using Accounting.Application;
using Accounting.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;
using TestSupport;

[assembly: AssemblyFixture(typeof(Accounting.IntegrationTests.AccountingApiFactory))]

namespace Accounting.IntegrationTests;

/// <summary>
/// Starts the real Accounting API against a real PostgreSQL container. The Estimates service is
/// replaced by an in-memory fake so these tests control what "the other service" returns.
/// The real HTTP client is covered by unit tests and by the Estimates consumer contract test.
/// </summary>
public sealed class AccountingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Audience = "accounting-api";

    private readonly PostgreSqlContainer _postgres = PostgresContainer.Create("accounting");

    public TestTokenIssuer Tokens { get; } = new();

    public FakeEstimatesServiceClient Estimates { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public GraphQLTestClient CreateGraphQLClient()
    {
        return new GraphQLTestClient(CreateClient());
    }

    public string CreateUserToken(string tenantId, params string[] scopes)
    {
        return Tokens.CreateUserToken(Audience, tenantId, scopes);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AccountingDb", _postgres.GetConnectionString());
        builder.UseSetting("Authentication:Authority", TestTokenIssuer.Issuer);
        builder.UseSetting("Authentication:Audience", Audience);
        builder.UseSetting("Hosting:EnforceHttps", "false");
        builder.UseSetting("Hosting:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("RateLimiting:PermitLimit", "10000");
        builder.UseSetting("MockAccountingProvider:SimulatedLatencyMilliseconds", "50");

        // Required settings for the real client, which these tests replace with a fake.
        builder.UseSetting("EstimatesService:BaseAddress", "http://estimates.test");
        builder.UseSetting("EstimatesService:TokenEndpoint", "http://identity.test/token");
        builder.UseSetting("EstimatesService:ClientSecret", "unused-in-tests");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestTokenIssuer.Issuer };
                configuration.SigningKeys.Add(Tokens.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.RemoveAll<IEstimatesServiceClient>();
            services.AddSingleton<IEstimatesServiceClient>(Estimates);
        });
    }
}

/// <summary>Thread-safe in-memory stand-in for the Estimates service.</summary>
public sealed class FakeEstimatesServiceClient : IEstimatesServiceClient
{
    private readonly ConcurrentDictionary<(string TenantId, Guid EstimateId), EstimateSnapshot> _estimates = new();
    private readonly ConcurrentDictionary<Guid, bool> _unavailableEstimates = new();

    public void AddEstimate(string tenantId, EstimateSnapshot estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        _estimates[(tenantId, estimate.Id)] = estimate;
    }

    /// <summary>Makes lookups of this estimate fail as if the Estimates service were down.</summary>
    public void SimulateOutageFor(Guid estimateId)
    {
        _unavailableEstimates[estimateId] = true;
    }

    public async Task<EstimateSnapshot?> GetEstimateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
    {
        // A small delay widens the window for concurrent-request tests.
        await Task.Delay(20, cancellationToken);

        if (_unavailableEstimates.ContainsKey(estimateId))
        {
            throw new EstimatesServiceUnavailableException("Simulated Estimates service outage.");
        }

        _estimates.TryGetValue((tenantId, estimateId), out EstimateSnapshot? estimate);
        return estimate;
    }

    public static EstimateSnapshot CreateEstimate(string status = EstimateSnapshot.ApprovedStatus, string customerName = "Acme Builders")
    {
        var line = new EstimateLineSnapshot("Framing", 10m, 45.50m, 10m, 455.00m, 45.50m, 500.50m);
        return new EstimateSnapshot(Guid.NewGuid(), status, "AUD", customerName, null, 455.00m, 45.50m, 500.50m, [line]);
    }
}

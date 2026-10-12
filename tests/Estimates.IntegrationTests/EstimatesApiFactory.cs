using Estimates.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;
using TestSupport;

[assembly: AssemblyFixture(typeof(Estimates.IntegrationTests.EstimatesApiFactory))]

namespace Estimates.IntegrationTests;

/// <summary>
/// Starts the real Estimates API in memory against a real PostgreSQL container.
/// One instance is shared by every test in the assembly; tests isolate themselves by using
/// a fresh, random tenant ID, which also exercises tenant isolation on every run.
/// </summary>
public sealed class EstimatesApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Audience = "estimates-api";

    private readonly PostgreSqlContainer _postgres = PostgresContainer.Create("estimates");

    public TestTokenIssuer Tokens { get; } = new();

    public SqlCommandCounter SqlCommands { get; } = new();

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
        // "Testing" uses the production-like appsettings.json defaults (no developer tools, no
        // introspection), with only the settings below overridden.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:EstimatesDb", _postgres.GetConnectionString());
        builder.UseSetting("Authentication:Authority", TestTokenIssuer.Issuer);
        builder.UseSetting("Authentication:Audience", Audience);
        builder.UseSetting("Hosting:EnforceHttps", "false");
        builder.UseSetting("Hosting:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("RateLimiting:PermitLimit", "10000");

        builder.ConfigureTestServices(services =>
        {
            // Trust the test signing key instead of downloading keys from an identity provider.
            // Everything else about token validation stays exactly as configured in production.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestTokenIssuer.Issuer };
                configuration.SigningKeys.Add(Tokens.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            // Lets tests count SQL commands (used to prove the DataLoader prevents N+1 queries).
            services.ConfigureDbContext<EstimatesDbContext>(options => options.AddInterceptors(SqlCommands));
        });
    }
}

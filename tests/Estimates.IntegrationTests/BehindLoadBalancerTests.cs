using System.Net;
using System.Text;
using Estimates.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Estimates.IntegrationTests;

/// <summary>
/// The API as deployed behind a TLS-terminating load balancer (AWS ALB): HTTPS enforced, the public
/// HTTPS port known, and forwarded headers enabled (what ASPNETCORE_FORWARDEDHEADERS_ENABLED and
/// ASPNETCORE_HTTPS_PORT do in the ECS task definition).
/// </summary>
public sealed class BehindLoadBalancerTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _proxiedFactory;
    private readonly HttpClient _client;
    private readonly EstimatesApiFactory _factory;

    public BehindLoadBalancerTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _proxiedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Hosting:EnforceHttps", "true");
            builder.UseSetting("https_port", "443");
            builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
        });
        _client = _proxiedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _proxiedFactory.Dispose();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Plain_http_health_checks_are_not_redirected(string path)
    {
        // ALB health checks call the container directly over HTTP, without X-Forwarded-Proto.
        using HttpResponseMessage response = await _client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Plain_http_graphql_is_redirected_to_https()
    {
        using HttpResponseMessage response = await PostGraphQLAsync(forwardedProto: null);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location?.Scheme);
    }

    [Fact]
    public async Task Https_terminated_at_the_load_balancer_reaches_the_api_without_a_redirect_loop()
    {
        using HttpResponseMessage response = await PostGraphQLAsync(forwardedProto: "https");

        // Not redirected, and authentication is still required.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Migration_mode_reports_success_when_schema_is_up_to_date()
    {
        int exitCode = await DatabaseMigrator.MigrateAsync(_factory.Services, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    private async Task<HttpResponseMessage> PostGraphQLAsync(string? forwardedProto)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/graphql", UriKind.Relative))
        {
            Content = new StringContent("""{"query":"{ __typename }"}""", Encoding.UTF8, "application/json"),
        };
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

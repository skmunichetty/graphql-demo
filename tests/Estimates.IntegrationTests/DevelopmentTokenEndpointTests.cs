using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceDefaults.Security;

namespace Estimates.IntegrationTests;

/// <summary>
/// POST /dev/token is a password-grant helper for developer machines. These tests prove it cannot be
/// reached, or even switched on, outside the Development environment.
/// </summary>
public sealed class DevelopmentTokenEndpointTests
{
    private readonly EstimatesApiFactory _factory;

    public DevelopmentTokenEndpointTests(EstimatesApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Endpoint_is_not_mapped_by_default()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            DevelopmentTokenEndpoint.Route,
            new { username = "alice" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Enabling_endpoint_outside_development_stops_startup()
    {
        using WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DevelopmentToken:Enabled", "true");
            builder.UseSetting("DevelopmentToken:TokenEndpoint", "http://localhost:8180/realms/construction/protocol/openid-connect/token");
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("only allowed in the Development environment", exception.Message, StringComparison.Ordinal);
    }
}

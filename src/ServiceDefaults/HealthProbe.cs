namespace ServiceDefaults;

/// <summary>
/// A container health check that needs no curl or wget (the ASP.NET runtime image has neither).
/// The orchestrator runs the same image with "--health-check"; this process calls the running
/// server's liveness endpoint on localhost and exits 0 (healthy) or 1 (unhealthy).
///
/// Example (ECS task definition): ["CMD", "dotnet", "/app/Estimates.Api.dll", "--health-check"]
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--health-check";

    public static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(Argument, StringComparer.Ordinal);
    }

    public static async Task<int> RunAsync()
    {
        // ASPNETCORE_HTTP_PORTS is set in the Dockerfile (8080). Use the first port if several are listed.
        string ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        string port = ports.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        var liveness = new Uri($"http://127.0.0.1:{port}{HttpSetup.HealthPathPrefix}/live");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(liveness);
            Console.WriteLine($"Health probe {liveness}: HTTP {(int)response.StatusCode}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"Health probe {liveness} failed: {exception.GetType().Name}");
            return 1;
        }
    }
}

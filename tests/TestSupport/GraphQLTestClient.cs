using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TestSupport;

/// <summary>The raw HTTP result of a GraphQL request, with the JSON body parsed.</summary>
public sealed record GraphQLResponse(HttpStatusCode StatusCode, JsonNode? Body)
{
    public JsonNode? Data => Body?["data"];

    public JsonArray? Errors => Body?["errors"]?.AsArray();

    public string? FirstErrorCode => Errors?[0]?["extensions"]?["code"]?.GetValue<string>();
}

/// <summary>Sends GraphQL operations over HTTP exactly as an external client would.</summary>
public sealed class GraphQLTestClient
{
    private readonly HttpClient _httpClient;

    public GraphQLTestClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GraphQLResponse> SendAsync(
        string query,
        object? variables,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");
        request.Content = JsonContent.Create(new { query, variables });
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/graphql-response+json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
        return new GraphQLResponse(response.StatusCode, body);
    }
}

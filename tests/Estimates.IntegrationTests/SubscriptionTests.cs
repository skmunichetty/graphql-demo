using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.TestHost;
using TestSupport;
using static Estimates.IntegrationTests.EstimateTestHelpers;

namespace Estimates.IntegrationTests;

/// <summary>
/// estimateChanged over both transports: Server-Sent Events on /graphql and WebSocket
/// (graphql-transport-ws) on /graphql/ws. Each test uses fresh tenants, so events from other tests
/// running in parallel can never arrive on these streams.
/// </summary>
public sealed class SubscriptionTests
{
    private const string EstimateChanged = """
        subscription EstimateChanged($estimateId: UUID) {
          estimateChanged(estimateId: $estimateId) {
            kind
            estimate { id status version total lineItems { description } }
          }
        }
        """;

    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly EstimatesApiFactory _factory;
    private readonly GraphQLTestClient _client;

    public SubscriptionTests(EstimatesApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateGraphQLClient();
    }

    [Fact]
    public async Task Sse_subscriber_receives_each_saved_change_in_order()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        await using SseSubscription subscription = await SseSubscription.OpenAsync(_factory.CreateClient(), EstimateChanged, new { }, token);

        JsonNode created = await CreateUntilFirstEventAsync(subscription, token);
        JsonNode withLine = await AddLineItemAsync(_client, token, created, "Concrete slab", 10m, 120m, 10m);
        await ApproveAsync(_client, token, withLine);

        JsonNode? lineEvent = await subscription.NextAsync(EventTimeout);
        JsonNode? approveEvent = await subscription.NextAsync(EventTimeout);

        Assert.Equal("LINE_ITEM_ADDED", lineEvent?["kind"]?.GetValue<string>());
        Assert.Equal("Concrete slab", lineEvent?["estimate"]?["lineItems"]?[0]?["description"]?.GetValue<string>());
        Assert.Equal("APPROVED", approveEvent?["kind"]?.GetValue<string>());
        Assert.Equal("APPROVED", approveEvent?["estimate"]?["status"]?.GetValue<string>());
        Assert.Equal(1320m, approveEvent?["estimate"]?["total"]?.GetValue<decimal>());
    }

    [Fact]
    public async Task Subscriber_never_receives_another_tenants_changes()
    {
        string tenantAToken = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        string tenantBToken = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        await using SseSubscription tenantB = await SseSubscription.OpenAsync(_factory.CreateClient(), EstimateChanged, new { }, tenantBToken);

        // Tenant A changes first; tenant B then changes its own estimate until B sees an event.
        // B's first event must be B's own estimate: A's change must never have been delivered.
        JsonNode tenantAEstimate = await CreateEstimateAsync(_client, tenantAToken, "Tenant A");
        JsonNode tenantBEstimate = await CreateUntilFirstEventAsync(tenantB, tenantBToken);

        Assert.NotEqual(tenantAEstimate["id"]!.GetValue<string>(), tenantBEstimate["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task EstimateId_argument_filters_to_one_estimate()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        JsonNode followed = await CreateEstimateAsync(_client, token, "Followed");
        JsonNode other = await CreateEstimateAsync(_client, token, "Other");
        await using SseSubscription subscription = await SseSubscription.OpenAsync(
            _factory.CreateClient(), EstimateChanged, new { estimateId = followed["id"]!.GetValue<string>() }, token);

        // Keep changing both estimates until the followed one reports a change; only it may appear.
        JsonNode? change = null;
        for (int attempt = 0; attempt < 20 && change is null; attempt++)
        {
            other = await AddLineItemAsync(_client, token, other, "Ignored", 1m, 1m, 10m);
            followed = await AddLineItemAsync(_client, token, followed, "Followed", 1m, 1m, 10m);
            change = await subscription.NextAsync(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(followed["id"]!.GetValue<string>(), change?["estimate"]?["id"]?.GetValue<string>());
    }

    [Fact]
    public async Task Token_without_read_scope_cannot_subscribe()
    {
        string token = _factory.CreateUserToken(NewTenantId(), "estimates:write");
        await using SseSubscription subscription = await SseSubscription.OpenAsync(_factory.CreateClient(), EstimateChanged, new { }, token);

        JsonNode? result = await subscription.NextResultAsync(EventTimeout);

        Assert.Equal("AUTH_NOT_AUTHORIZED", result?["errors"]?[0]?["extensions"]?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task Sse_subscription_without_token_is_rejected_with_401()
    {
        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = SseSubscription.CreateRequest(EstimateChanged, new { }, accessToken: null);

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebSocket_subscriber_authenticated_in_connection_init_receives_changes()
    {
        string token = _factory.CreateUserToken(NewTenantId(), ReadWriteScopes);
        using WebSocket socket = await ConnectWebSocketAsync();

        await SendAsync(socket, new { type = "connection_init", payload = new { Authorization = $"Bearer {token}" } });
        Assert.Equal("connection_ack", (await ReceiveAsync(socket))?["type"]?.GetValue<string>());

        await SendAsync(socket, new { id = "1", type = "subscribe", payload = new { query = EstimateChanged } });

        // Create estimates until the first "next" message arrives (the subscription is now live).
        JsonNode? message = null;
        for (int attempt = 0; attempt < 20 && message is null; attempt++)
        {
            await CreateEstimateAsync(_client, token, "Via WebSocket");
            message = await ReceiveAsync(socket, TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal("next", message?["type"]?.GetValue<string>());
        Assert.Equal("CREATED", message?["payload"]?["data"]?["estimateChanged"]?["kind"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer not-a-jwt")]
    public async Task WebSocket_without_valid_token_is_closed_before_any_operation(string? authorization)
    {
        using WebSocket socket = await ConnectWebSocketAsync();

        object payload = authorization is null ? new { } : new { Authorization = authorization };
        await SendAsync(socket, new { type = "connection_init", payload });
        JsonNode? reply = await ReceiveAsync(socket);

        Assert.Null(reply);
        Assert.Equal(WebSocketState.CloseReceived, socket.State);
    }

    /// <summary>
    /// The SSE response starts before the subscription is registered, so keep creating estimates
    /// until the first event arrives. Returns the estimate from that first CREATED event.
    /// </summary>
    private async Task<JsonNode> CreateUntilFirstEventAsync(SseSubscription subscription, string token)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await CreateEstimateAsync(_client, token, "Subscriber test");
            JsonNode? change = await subscription.NextAsync(TimeSpan.FromMilliseconds(500));
            if (change is not null)
            {
                Assert.Equal("CREATED", change["kind"]!.GetValue<string>());
                string id = change["estimate"]!["id"]!.GetValue<string>();

                // Re-read the estimate so callers get its current version and fields.
                GraphQLResponse response = await _client.SendAsync(
                    "query($id: UUID!) { estimate(id: $id) { ...EstimateFields } }" + Operations.EstimateFields,
                    new { id },
                    token,
                    TestContext.Current.CancellationToken);
                return response.Data!["estimate"]!;
            }
        }

        throw new TimeoutException("No subscription event was received.");
    }

    private async Task<WebSocket> ConnectWebSocketAsync()
    {
        WebSocketClient client = _factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, "graphql/ws"), TestContext.Current.CancellationToken);
    }

    private static async Task SendAsync(WebSocket socket, object message)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, TestContext.Current.CancellationToken);
    }

    /// <summary>Returns the next message, or null on timeout or when the server closes the socket.</summary>
    private static async Task<JsonNode?> ReceiveAsync(WebSocket socket, TimeSpan? timeout = null)
    {
        using var timeoutSource = new CancellationTokenSource(timeout ?? EventTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, TestContext.Current.CancellationToken);
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, linked.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()));

                    // Keep-alive pings carry no data.
                    if (node?["type"]?.GetValue<string>() is "ping" or "pong")
                    {
                        message.SetLength(0);
                        continue;
                    }

                    return node;
                }
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>A GraphQL subscription read as Server-Sent Events on a background task.</summary>
    private sealed class SseSubscription : IAsyncDisposable
    {
        private readonly HttpClient _client;
        private readonly HttpResponseMessage _response;
        private readonly Channel<JsonNode> _results = Channel.CreateUnbounded<JsonNode>();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _reader;

        private SseSubscription(HttpClient client, HttpResponseMessage response)
        {
            _client = client;
            _response = response;
            _reader = Task.Run(ReadEventsAsync);
        }

        public static HttpRequestMessage CreateRequest(string query, object variables, string? accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = JsonContent.Create(new { query, variables }) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return request;
        }

        public static async Task<SseSubscription> OpenAsync(HttpClient client, string query, object variables, string accessToken)
        {
            using HttpRequestMessage request = CreateRequest(query, variables, accessToken);
            HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            return new SseSubscription(client, response);
        }

        /// <summary>The next estimateChanged payload, or null if none arrives in time.</summary>
        public async Task<JsonNode?> NextAsync(TimeSpan timeout)
        {
            JsonNode? result = await NextResultAsync(timeout);
            return result?["data"]?["estimateChanged"];
        }

        /// <summary>The next complete GraphQL result (data and/or errors), or null if none arrives in time.</summary>
        public async Task<JsonNode?> NextResultAsync(TimeSpan timeout)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                return await _results.Reader.ReadAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _response.Dispose();
            try
            {
                await _reader;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Expected when the stream is torn down.
            }

            _stop.Dispose();
            _client.Dispose();
        }

        private async Task ReadEventsAsync()
        {
            await using Stream stream = await _response.Content.ReadAsStreamAsync(_stop.Token);
            using var reader = new StreamReader(stream);
            string eventName = "message";
            var data = new StringBuilder();
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (line.Length == 0)
                {
                    if (eventName == "next" && data.Length > 0)
                    {
                        await _results.Writer.WriteAsync(JsonNode.Parse(data.ToString())!, _stop.Token);
                    }

                    eventName = "message";
                    data.Clear();
                }
                else if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(line["data:".Length..].Trim());
                }
            }
        }
    }
}

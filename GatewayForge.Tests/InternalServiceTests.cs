extern alias InternalApp;

using System.Net;
using System.Net.Http.Json;
using GatewayForge.Shared;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GatewayForge.Tests;

public class InternalServiceTests : IClassFixture<WebApplicationFactory<InternalApp::Program>>
{
    private readonly HttpClient _client;

    public InternalServiceTests(WebApplicationFactory<InternalApp::Program> factory)
    {
        TestHelpers.EnsureEnvironment();
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Request(string? gatewayKey, string? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/internal-data");
        if (gatewayKey is not null)
        {
            request.Headers.Add(GatewayHeaders.GatewayKey, gatewayKey);
        }

        if (userId is not null)
        {
            request.Headers.Add(GatewayHeaders.UserId, userId);
        }

        return request;
    }

    [Fact]
    public async Task Rejects_callers_that_did_not_come_through_the_gateway()
    {
        var response = await _client.SendAsync(Request(gatewayKey: null, userId: "admin-root"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_wrong_gateway_key()
    {
        var response = await _client.SendAsync(Request("wrong-key", "admin-root"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_the_stamped_user_when_the_gateway_key_is_valid()
    {
        var response = await _client.SendAsync(Request(TestHelpers.SharedKey, "user-123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("user-123", body!["userId"]);
    }

    [Fact]
    public async Task Health_endpoint_is_open_for_container_probes()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Echoes_a_correlation_id()
    {
        var request = Request(TestHelpers.SharedKey, "u");
        request.Headers.Add(GatewayHeaders.CorrelationId, "trace-abc-123");

        var response = await _client.SendAsync(request);

        Assert.Equal("trace-abc-123", response.Headers.GetValues(GatewayHeaders.CorrelationId).Single());
    }
}

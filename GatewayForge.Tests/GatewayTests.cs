extern alias GatewayApp;

using System.Net;
using GatewayForge.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace GatewayForge.Tests;

internal sealed class CapturingForwarderFactory : IForwarderHttpClientFactory
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
        new(new CapturingHandler(Requests));

    private sealed class CapturingHandler(List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}

public class GatewayTests : IClassFixture<WebApplicationFactory<GatewayApp::Program>>
{
    private readonly WebApplicationFactory<GatewayApp::Program> _baseFactory;

    public GatewayTests(WebApplicationFactory<GatewayApp::Program> factory)
    {
        TestHelpers.EnsureEnvironment();
        _baseFactory = factory;
    }

    private (HttpClient Client, CapturingForwarderFactory Downstream) CreateClient(Dictionary<string, string?>? settings = null)
    {
        var downstream = new CapturingForwarderFactory();
        var client = _baseFactory.WithWebHostBuilder(builder =>
        {
            if (settings is not null)
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            }

            builder.ConfigureServices(services =>
            {
                services.UseTestAuthentication();
                services.AddSingleton<IForwarderHttpClientFactory>(downstream);
            });
        }).CreateClient();

        return (client, downstream);
    }

    private static HttpRequestMessage Get(string path, string? user = null, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
        }

        if (roles is not null)
        {
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        }

        return request;
    }

    [Theory]
    [InlineData("/api/secure/secure-data")]
    [InlineData("/api/internal/internal-data")]
    public async Task Unauthenticated_requests_get_401_and_never_reach_downstream(string path)
    {
        var (client, downstream) = CreateClient();

        var response = await client.SendAsync(Get(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Theory]
    [InlineData("/api/secure/secure-data", GatewayRoles.InternalUser)]
    [InlineData("/api/internal/internal-data", GatewayRoles.SecureUser)]
    [InlineData("/api/internal/internal-data", null)]
    public async Task Authenticated_users_without_the_route_role_get_403(string path, string? roles)
    {
        var (client, downstream) = CreateClient();

        var response = await client.SendAsync(Get(path, user: "u1", roles: roles));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task Internal_route_overwrites_a_spoofed_user_id_and_stamps_the_gateway_key()
    {
        var (client, downstream) = CreateClient();
        var request = Get("/api/internal/internal-data", user: "real-user", roles: GatewayRoles.InternalUser);
        request.Headers.Add(GatewayHeaders.UserId, "attacker-spoofed");
        request.Headers.Add(GatewayHeaders.GatewayKey, "attacker-guess");
        request.Headers.Add("Authorization", "Bearer should-be-stripped");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(downstream.Requests);
        Assert.Equal(new[] { "real-user" }, forwarded.Headers.GetValues(GatewayHeaders.UserId));
        Assert.Equal(new[] { TestHelpers.SharedKey }, forwarded.Headers.GetValues(GatewayHeaders.GatewayKey));
        Assert.False(forwarded.Headers.Contains("Authorization"));
        Assert.Equal("/api/internal-data", forwarded.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Secure_route_forwards_the_token_but_strips_caller_made_identity_headers()
    {
        var (client, downstream) = CreateClient();
        var request = Get("/api/secure/secure-data", user: "real-user", roles: GatewayRoles.SecureUser);
        request.Headers.Add(GatewayHeaders.UserId, "attacker-spoofed");
        request.Headers.Add(GatewayHeaders.GatewayKey, "attacker-guess");
        request.Headers.Add("Authorization", "Bearer relayed-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(downstream.Requests);
        Assert.False(forwarded.Headers.Contains(GatewayHeaders.UserId));
        Assert.False(forwarded.Headers.Contains(GatewayHeaders.GatewayKey));
        Assert.Equal("Bearer relayed-token", forwarded.Headers.GetValues("Authorization").Single());
        Assert.Equal("/api/secure-data", forwarded.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Correlation_id_is_forwarded_and_echoed()
    {
        var (client, downstream) = CreateClient();
        var request = Get("/api/secure/secure-data", user: "u", roles: GatewayRoles.SecureUser);
        request.Headers.Add(GatewayHeaders.CorrelationId, "trace-xyz-789");

        var response = await client.SendAsync(request);

        Assert.Equal("trace-xyz-789", response.Headers.GetValues(GatewayHeaders.CorrelationId).Single());
        var forwarded = Assert.Single(downstream.Requests);
        Assert.Equal("trace-xyz-789", forwarded.Headers.GetValues(GatewayHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task A_malformed_correlation_id_is_replaced()
    {
        var (client, _) = CreateClient();
        var request = Get("/api/secure/secure-data", user: "u", roles: GatewayRoles.SecureUser);
        request.Headers.TryAddWithoutValidation(GatewayHeaders.CorrelationId, "bad id with spaces");

        var response = await client.SendAsync(request);

        var echoed = response.Headers.GetValues(GatewayHeaders.CorrelationId).Single();
        Assert.NotEqual("bad id with spaces", echoed);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", echoed);
    }

    [Fact]
    public async Task Requests_beyond_the_limit_get_429()
    {
        var (client, downstream) = CreateClient(new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = "2",
            ["RateLimiting:WindowSeconds"] = "60"
        });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.SendAsync(
                Get("/api/secure/secure-data", user: "limited-user", roles: GatewayRoles.SecureUser));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Equal(2, downstream.Requests.Count);
    }

    [Fact]
    public async Task Rate_limits_are_per_user()
    {
        var (client, _) = CreateClient(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1" });

        var first = await client.SendAsync(Get("/api/secure/secure-data", "user-a", GatewayRoles.SecureUser));
        var firstAgain = await client.SendAsync(Get("/api/secure/secure-data", "user-a", GatewayRoles.SecureUser));
        var other = await client.SendAsync(Get("/api/secure/secure-data", "user-b", GatewayRoles.SecureUser));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_open_for_container_probes()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

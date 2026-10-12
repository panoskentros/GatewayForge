extern alias SecureApp;

using System.Net;
using System.Net.Http.Json;
using GatewayForge.Shared;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GatewayForge.Tests;

public class SecureServiceTests : IClassFixture<WebApplicationFactory<SecureApp::Program>>
{
    private readonly HttpClient _client;

    public SecureServiceTests(WebApplicationFactory<SecureApp::Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => services.UseTestAuthentication()))
            .CreateClient();
    }

    private static HttpRequestMessage Request(string? user, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/secure-data");
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

    [Fact]
    public async Task Rejects_requests_with_no_credentials()
    {
        var response = await _client.SendAsync(Request(user: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_authenticated_users_without_the_secure_role()
    {
        var response = await _client.SendAsync(Request("u1", roles: GatewayRoles.InternalUser));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Allows_users_with_the_secure_role()
    {
        var response = await _client.SendAsync(Request("u1", roles: GatewayRoles.SecureUser));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("name-of-u1", body!["user"]);
    }

    [Fact]
    public async Task Health_endpoint_is_open_for_container_probes()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

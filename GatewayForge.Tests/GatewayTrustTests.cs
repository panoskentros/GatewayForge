using System.Net.Http.Headers;
using GatewayForge.Shared;
using Microsoft.AspNetCore.Http;

namespace GatewayForge.Tests;

public class GatewayTrustTests
{
    private static HttpRequestHeaders NewHeaders() => new HttpRequestMessage().Headers;

    [Fact]
    public void Stamp_replaces_a_client_supplied_user_id_instead_of_appending()
    {
        var headers = NewHeaders();
        headers.TryAddWithoutValidation(GatewayHeaders.UserId, "attacker");

        GatewayTrust.Stamp(headers, "real-user", "key");

        Assert.Equal(new[] { "real-user" }, headers.GetValues(GatewayHeaders.UserId));
    }

    [Fact]
    public void Stamp_without_a_user_still_clears_the_client_supplied_user_id()
    {
        var headers = NewHeaders();
        headers.TryAddWithoutValidation(GatewayHeaders.UserId, "attacker");

        GatewayTrust.Stamp(headers, null, "key");

        Assert.False(headers.Contains(GatewayHeaders.UserId));
    }

    [Fact]
    public void Stamp_replaces_a_client_supplied_gateway_key()
    {
        var headers = NewHeaders();
        headers.TryAddWithoutValidation(GatewayHeaders.GatewayKey, "guess");

        GatewayTrust.Stamp(headers, "u", "real-key");

        Assert.Equal(new[] { "real-key" }, headers.GetValues(GatewayHeaders.GatewayKey));
    }

    [Fact]
    public void RemoveClientSuppliedHeaders_strips_both_identity_headers()
    {
        var headers = NewHeaders();
        headers.TryAddWithoutValidation(GatewayHeaders.UserId, "attacker");
        headers.TryAddWithoutValidation(GatewayHeaders.GatewayKey, "guess");

        GatewayTrust.RemoveClientSuppliedHeaders(headers);

        Assert.False(headers.Contains(GatewayHeaders.UserId));
        Assert.False(headers.Contains(GatewayHeaders.GatewayKey));
    }

    private static HttpRequest RequestWith(params string[] keys)
    {
        var context = new DefaultHttpContext();
        if (keys.Length > 0)
        {
            context.Request.Headers[GatewayHeaders.GatewayKey] = keys;
        }

        return context.Request;
    }

    [Fact]
    public void IsFromGateway_accepts_the_matching_key() =>
        Assert.True(GatewayTrust.IsFromGateway(RequestWith("secret"), "secret"));

    [Fact]
    public void IsFromGateway_rejects_a_missing_key() =>
        Assert.False(GatewayTrust.IsFromGateway(RequestWith(), "secret"));

    [Fact]
    public void IsFromGateway_rejects_a_wrong_key() =>
        Assert.False(GatewayTrust.IsFromGateway(RequestWith("not-secret"), "secret"));

    [Fact]
    public void IsFromGateway_rejects_an_empty_key() =>
        Assert.False(GatewayTrust.IsFromGateway(RequestWith(""), "secret"));

    [Fact]
    public void IsFromGateway_rejects_duplicate_headers_even_if_one_matches() =>
        Assert.False(GatewayTrust.IsFromGateway(RequestWith("secret", "secret"), "secret"));
}

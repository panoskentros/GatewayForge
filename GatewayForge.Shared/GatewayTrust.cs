using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace GatewayForge.Shared;

public static class GatewayTrust
{
    public static void RemoveClientSuppliedHeaders(HttpRequestHeaders headers)
    {
        headers.Remove(GatewayHeaders.UserId);
        headers.Remove(GatewayHeaders.GatewayKey);
    }

    public static void Stamp(HttpRequestHeaders headers, string? userId, string sharedKey)
    {
        RemoveClientSuppliedHeaders(headers);

        if (!string.IsNullOrEmpty(userId))
        {
            headers.TryAddWithoutValidation(GatewayHeaders.UserId, userId);
        }

        headers.TryAddWithoutValidation(GatewayHeaders.GatewayKey, sharedKey);
    }

    public static string? GetUserId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static bool IsFromGateway(HttpRequest request, string expectedKey)
    {
        if (!request.Headers.TryGetValue(GatewayHeaders.GatewayKey, out var values) || values.Count != 1)
        {
            return false;
        }

        var provided = values[0];
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expectedKey));
    }
}

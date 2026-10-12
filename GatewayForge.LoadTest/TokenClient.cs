using System.Text.Json;

namespace GatewayForge.LoadTest;

internal static class TokenClient
{
    public static async Task<string> GetAsync(HttpClient client, Options options)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "gateway-client",
            ["grant_type"] = "password",
            ["username"] = options.User,
            ["password"] = options.Password
        });

        using var response = await client.PostAsync(
            $"{options.Keycloak}/realms/gateway-forge/protocol/openid-connect/token", form);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()!;
    }
}

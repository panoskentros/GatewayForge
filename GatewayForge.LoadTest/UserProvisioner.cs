using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GatewayForge.LoadTest;

internal sealed record VirtualUser(int Index, string Username, string Token);

internal static class UserProvisioner
{
    private const string Prefix = "loadtest-";
    private const string Realm = "gateway-forge";

    public static async Task<string> AdminTokenAsync(HttpClient client, Options options)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "admin-cli",
            ["grant_type"] = "password",
            ["username"] = options.AdminUser,
            ["password"] = options.AdminPassword
        });

        using var response = await client.PostAsync(
            $"{options.Keycloak}/realms/master/protocol/openid-connect/token", form);
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    public static async Task RemoveAsync(HttpClient client, Options options, string adminToken)
    {
        var listing = await SendAsync(client, HttpMethod.Get,
            $"{options.Keycloak}/admin/realms/{Realm}/users?search={Prefix}&max=1000", adminToken);
        using var document = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());

        var ids = document.RootElement.EnumerateArray()
            .Where(user => user.GetProperty("username").GetString()!.StartsWith(Prefix))
            .Select(user => user.GetProperty("id").GetString()!)
            .ToList();

        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync();
            try
            {
                using var response = await SendAsync(client, HttpMethod.Delete,
                    $"{options.Keycloak}/admin/realms/{Realm}/users/{id}", adminToken);
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    public static async Task<List<VirtualUser>> CreateAsync(
        HttpClient client, Options options, string adminToken, int count)
    {
        var roles = new List<JsonElement>();
        foreach (var role in new[] { "secure-user", "internal-user" })
        {
            using var response = await SendAsync(client, HttpMethod.Get,
                $"{options.Keycloak}/admin/realms/{Realm}/roles/{role}", adminToken);
            response.EnsureSuccessStatusCode();
            roles.Add(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
        }

        var rolesJson = JsonSerializer.Serialize(roles);
        var password = Guid.NewGuid().ToString("N");
        using var gate = new SemaphoreSlim(8);

        var tasks = Enumerable.Range(1, count).Select(async index =>
        {
            await gate.WaitAsync();
            try
            {
                var username = $"{Prefix}{index:D3}";
                var payload = JsonSerializer.Serialize(new
                {
                    username,
                    enabled = true,
                    emailVerified = true,
                    firstName = "Load",
                    lastName = $"User{index}",
                    email = $"{username}@example.test",
                    credentials = new[] { new { type = "password", value = password, temporary = false } }
                });

                using var created = await SendAsync(client, HttpMethod.Post,
                    $"{options.Keycloak}/admin/realms/{Realm}/users", adminToken, payload);
                created.EnsureSuccessStatusCode();
                var id = created.Headers.Location!.Segments.Last();

                using var mapped = await SendAsync(client, HttpMethod.Post,
                    $"{options.Keycloak}/admin/realms/{Realm}/users/{id}/role-mappings/realm", adminToken, rolesJson);
                mapped.EnsureSuccessStatusCode();

                var token = await PasswordTokenAsync(client, options, username, password);
                return new VirtualUser(index, username, token);
            }
            finally
            {
                gate.Release();
            }
        });

        return (await Task.WhenAll(tasks)).OrderBy(user => user.Index).ToList();
    }

    private static async Task<string> PasswordTokenAsync(
        HttpClient client, Options options, string username, string password)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "gateway-client",
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password
        });

        using var response = await client.PostAsync(
            $"{options.Keycloak}/realms/{Realm}/protocol/openid-connect/token", form);
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string adminToken, string? json = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }
}

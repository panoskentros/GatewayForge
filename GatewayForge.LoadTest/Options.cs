namespace GatewayForge.LoadTest;

internal sealed record Options(
    string Gateway,
    string Keycloak,
    string User,
    string Password,
    string AdminUser,
    string AdminPassword,
    int Concurrency,
    int Users,
    int ThinkMs,
    int DurationSeconds,
    int WarmupSeconds,
    int Rounds,
    int Requests,
    string Levels,
    string Mode)
{
    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            values[args[i].TrimStart('-')] = args[i + 1];
        }

        string Get(string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

        return new Options(
            Get("gateway", "http://localhost:5100"),
            Get("keycloak", "http://localhost:8080"),
            Get("user", "testuser"),
            Get("password", "123456"),
            Get("admin-user", "admin"),
            Get("admin-password", "admin"),
            int.Parse(Get("concurrency", "64")),
            int.Parse(Get("users", "50")),
            int.Parse(Get("think", "0")),
            int.Parse(Get("duration", "15")),
            int.Parse(Get("warmup", "3")),
            int.Parse(Get("rounds", "1")),
            int.Parse(Get("requests", "500000")),
            Get("levels", "16,64,256,1024"),
            Get("mode", "compare").ToLowerInvariant());
    }
}

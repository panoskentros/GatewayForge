using System.Globalization;
using GatewayForge.LoadTest;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args);

var levels = options.Levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .ToArray();

using var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = Math.Max(options.Concurrency, levels.Max()),
    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
};
using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };

string token;
try
{
    token = await TokenClient.GetAsync(client, options);
}
catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
{
    Console.Error.WriteLine($"Could not get a token. Is the stack running? {exception.Message}");
    return 1;
}

var duration = TimeSpan.FromSeconds(options.DurationSeconds);
var warmup = TimeSpan.FromSeconds(options.WarmupSeconds);

if (options.Mode is "users" or "noisy")
{
    return await RunMultiUserAsync(client, options);
}

if (options.Mode is "volume" or "sweep")
{
    var pair = new[]
    {
        new Scenario("secure (re-authenticates)", $"{options.Gateway}/api/secure/secure-data", true),
        new Scenario("internal (edge offload)", $"{options.Gateway}/api/internal/internal-data", true)
    };

    foreach (var scenario in pair)
    {
        await LoadRunner.RunAsync(client, scenario, token, options.Concurrency, TimeSpan.FromSeconds(options.WarmupSeconds));
    }

    if (options.Mode == "volume")
    {
        Console.WriteLine(
            $"Volume: {options.Requests:N0} requests per path at {options.Concurrency} concurrent connections.");

        var finished = new List<RunResult>();
        foreach (var scenario in pair)
        {
            var result = await LoadRunner.RunCountAsync(
                client, scenario, token, options.Concurrency, options.Requests);
            finished.Add(result);
            Console.WriteLine(
                $"  {scenario.Name}: {result.Total:N0} requests in {result.Seconds:F1}s " +
                $"({result.RequestsPerSecond:N0} req/s)");
        }

        Report.PrintComparison(finished);
        return 0;
    }

    Console.WriteLine(
        $"Sweep: {options.DurationSeconds}s per path at each concurrency level, " +
        "every connection busy (closed loop).");
    Console.WriteLine();
    Console.WriteLine(
        $"{"conns",7}{"secure req/s",14}{"internal req/s",16}{"gain",8}" +
        $"{"p50 s/i ms",16}{"p99 s/i ms",20}{"errors",8}");
    Console.WriteLine(new string('-', 89));

    var highestClean = 0;
    foreach (var level in levels)
    {
        var ramp = TimeSpan.FromSeconds(options.RampSeconds >= 0 ? options.RampSeconds : Math.Clamp(level / 2000, 1, 40));
        var measured = TimeSpan.FromSeconds(options.DurationSeconds);

        var secureRun = await LoadRunner.RunAsync(
            client, pair[0], token, level, measured, 0, ramp);
        var internalRun = await LoadRunner.RunAsync(
            client, pair[1], token, level, measured, 0, TimeSpan.Zero);

        var errors = secureRun.NonSuccess + internalRun.NonSuccess;
        Console.WriteLine(
            $"{level,7}{secureRun.RequestsPerSecond,14:N0}{internalRun.RequestsPerSecond,16:N0}" +
            $"{internalRun.RequestsPerSecond / secureRun.RequestsPerSecond,7:F2}x" +
            $"{Report.Percentile(secureRun.Samples, 0.50),9:F1} /{Report.Percentile(internalRun.Samples, 0.50),6:F1}" +
            $"{Report.Percentile(secureRun.Samples, 0.99),12:F1} /{Report.Percentile(internalRun.Samples, 0.99),7:F1}" +
            $"{errors,8:N0}");

        if (errors > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Errors appeared at {level:N0} connections; stopping. Error kinds:");
            foreach (var kind in LoadRunner.DrainErrorKinds().OrderByDescending(pair => pair.Value))
            {
                Console.WriteLine($"  {kind.Value,8:N0}  {kind.Key}");
            }

            break;
        }

        highestClean = level;
    }

    Console.WriteLine();
    Console.WriteLine($"Highest level with 0 errors: {highestClean:N0} concurrent connections.");
    return 0;
}

if (options.Mode == "flood")
{
    var protectedRoute = new Scenario("internal (protected)", $"{options.Gateway}/api/internal/internal-data", true);
    Console.WriteLine(
        $"Flood: {options.Concurrency} concurrent connections, one user, {options.DurationSeconds}s " +
        "against the protected internal route with the default rate limit.");

    var flood = await LoadRunner.RunAsync(client, protectedRoute, token, options.Concurrency, duration);
    Report.PrintFlood(flood);
    return 0;
}

var scenarios = new[]
{
    new Scenario("secure (re-authenticates)", $"{options.Gateway}/api/secure/secure-data", true),
    new Scenario("internal (edge offload)", $"{options.Gateway}/api/internal/internal-data", true)
};

Console.WriteLine(
    $"Compare: {options.Concurrency} concurrent connections, {options.WarmupSeconds}s warmup, " +
    $"{options.Rounds} interleaved round(s) of {options.DurationSeconds}s per scenario.");

foreach (var scenario in scenarios)
{
    await LoadRunner.RunAsync(client, scenario, token, options.Concurrency, warmup);
}

var pooled = scenarios.ToDictionary(scenario => scenario.Name, _ => new List<RunResult>());
for (var round = 1; round <= options.Rounds; round++)
{
    var order = round % 2 == 1 ? scenarios : scenarios.Reverse().ToArray();
    foreach (var scenario in order)
    {
        var result = await LoadRunner.RunAsync(client, scenario, token, options.Concurrency, duration);
        pooled[scenario.Name].Add(result);
        Console.WriteLine($"  round {round}: {scenario.Name,-26}{result.RequestsPerSecond,9:N0} req/s");
    }
}

var results = scenarios.Select(scenario =>
{
    var runs = pooled[scenario.Name];
    return new RunResult(
        scenario.Name,
        runs.Sum(run => run.Seconds),
        runs.SelectMany(run => run.Samples).ToList());
}).ToList();

Report.PrintComparison(results);
return 0;

static async Task<int> RunMultiUserAsync(HttpClient client, Options options)
{
    var gateway = options.Gateway;
    var secure = new Route("secure (re-authenticates)", $"{gateway}/api/secure/secure-data", true);
    var internalRoute = new Route("internal (edge offload)", $"{gateway}/api/internal/internal-data", true);

    var adminToken = await UserProvisioner.AdminTokenAsync(client, options);
    await UserProvisioner.RemoveAsync(client, options, adminToken);

    var count = options.Mode == "noisy" ? options.Users + 1 : options.Users;
    Console.WriteLine($"Creating {count} users in Keycloak and fetching a token for each...");
    var users = await UserProvisioner.CreateAsync(client, options, adminToken, count);

    try
    {
        var duration = TimeSpan.FromSeconds(options.DurationSeconds);
        var warmup = TimeSpan.FromSeconds(options.WarmupSeconds);

        if (options.Mode == "users")
        {
            var phases = new List<Phase>();
            foreach (var route in new[] { secure, internalRoute })
            {
                Console.Write($"  {route.Name}: warming up... ");
                await UserLoad.RunAsync(client, users, (_, _) => route, (_, _) => options.ThinkMs, warmup);
                Console.Write("measuring... ");
                var (samples, seconds) = await UserLoad.RunAsync(
                    client, users, (_, _) => route, (_, _) => options.ThinkMs, duration);
                var phase = new Phase(route.Name, users.Count, samples, seconds);
                Console.WriteLine($"{phase.RequestsPerSecond:N0} req/s");
                phases.Add(phase);
            }

            UserReport.PrintPhases(phases);
            return 0;
        }

        var abuser = users[0].Index;
        var crowd = users.Skip(1).ToList();
        foreach (var user in users)
        {
            await Warm(client, user, internalRoute, secure);
        }

        Console.WriteLine($"Running {crowd.Count} paced users and 1 abuser for {options.DurationSeconds}s...");

        Route Pick(int user, Random random)
        {
            if (user == abuser)
            {
                return internalRoute;
            }

            return random.Next(100) < 50 ? internalRoute : secure;
        }

        int Think(int user, Random random) => user == abuser ? 0 : random.Next(800, 1600);

        var (all, elapsed) = await UserLoad.RunAsync(client, users, Pick, Think, duration);
        UserReport.PrintNoisyNeighbour(all, elapsed, abuser);
        return 0;
    }
    finally
    {
        Console.WriteLine();
        Console.WriteLine("Removing test users...");
        await UserProvisioner.RemoveAsync(client, options, adminToken);
    }
}

static async Task Warm(HttpClient client, VirtualUser user, params Route[] routes)
{
    foreach (var route in routes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route.Url);
        if (route.Authenticated)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
        }

        using var response = await client.SendAsync(request);
    }
}

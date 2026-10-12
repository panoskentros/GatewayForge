using System.Diagnostics;
using System.Net.Http.Headers;

namespace GatewayForge.LoadTest;

internal sealed record Scenario(string Name, string Url, bool Authenticated);

internal readonly record struct Sample(int Status, long Ticks);

internal sealed class RunResult(string name, double seconds, IReadOnlyList<Sample> samples)
{
    public string Name { get; } = name;
    public double Seconds { get; } = seconds;
    public IReadOnlyList<Sample> Samples { get; } = samples;
    public int Total => Samples.Count;
    public double RequestsPerSecond => Total / Seconds;
    public int NonSuccess => Samples.Count(sample => sample.Status is < 200 or >= 300);
}

internal static class LoadRunner
{
    public static async Task<RunResult> RunAsync(
        HttpClient client, Scenario scenario, string? token, int concurrency, TimeSpan duration)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = started + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            var samples = new List<Sample>(4096);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Url);
                if (scenario.Authenticated && token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var begin = Stopwatch.GetTimestamp();
                int status;
                try
                {
                    using var response = await client.SendAsync(request);
                    status = (int)response.StatusCode;
                }
                catch (HttpRequestException)
                {
                    status = 0;
                }

                samples.Add(new Sample(status, Stopwatch.GetTimestamp() - begin));
            }

            return samples;
        })).ToArray();

        var results = await Task.WhenAll(workers);
        var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        return new RunResult(scenario.Name, seconds, results.SelectMany(list => list).ToList());
    }

    public static async Task<RunResult> RunCountAsync(
        HttpClient client, Scenario scenario, string? token, int concurrency, int totalRequests)
    {
        var issued = 0;
        var started = Stopwatch.GetTimestamp();

        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            var samples = new List<Sample>(totalRequests / concurrency + 16);
            while (Interlocked.Increment(ref issued) <= totalRequests)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Url);
                if (scenario.Authenticated && token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var begin = Stopwatch.GetTimestamp();
                int status;
                try
                {
                    using var response = await client.SendAsync(request);
                    status = (int)response.StatusCode;
                }
                catch (HttpRequestException)
                {
                    status = 0;
                }

                samples.Add(new Sample(status, Stopwatch.GetTimestamp() - begin));
            }

            return samples;
        })).ToArray();

        var results = await Task.WhenAll(workers);
        var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        return new RunResult(scenario.Name, seconds, results.SelectMany(list => list).ToList());
    }
}

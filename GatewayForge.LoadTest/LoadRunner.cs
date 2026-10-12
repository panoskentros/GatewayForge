using System.Collections.Concurrent;
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
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMilliseconds(250);

    private static readonly ConcurrentDictionary<string, int> ErrorKinds = new();

    public static IReadOnlyDictionary<string, int> DrainErrorKinds()
    {
        var snapshot = ErrorKinds.ToDictionary(pair => pair.Key, pair => pair.Value);
        ErrorKinds.Clear();
        return snapshot;
    }

    private static void RecordError(Exception exception)
    {
        var root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var message = root.Message.Length > 70 ? root.Message[..70] : root.Message;
        ErrorKinds.AddOrUpdate($"{root.GetType().Name}: {message}", 1, (_, count) => count + 1);
    }

    public static async Task<RunResult> RunAsync(
        HttpClient client,
        Scenario scenario,
        string? token,
        int concurrency,
        TimeSpan duration,
        int thinkMilliseconds = 0,
        TimeSpan rampUp = default)
    {
        var started = Stopwatch.GetTimestamp();
        var measureFrom = started + (long)(rampUp.TotalSeconds * Stopwatch.Frequency);
        var deadline = measureFrom + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        var workers = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
        {
            if (rampUp > TimeSpan.Zero)
            {
                await Task.Delay(TimeSpan.FromTicks(rampUp.Ticks * index / concurrency));
            }

            var samples = new List<Sample>(256);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Url);
                if (scenario.Authenticated && token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var begin = Stopwatch.GetTimestamp();
                var failed = false;
                int status;
                try
                {
                    using var response = await client.SendAsync(request);
                    status = (int)response.StatusCode;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    status = 0;
                    failed = true;
                    RecordError(exception);
                }

                var isError = failed || status is < 200 or >= 300;
                if (isError || begin >= measureFrom)
                {
                    samples.Add(new Sample(status, Stopwatch.GetTimestamp() - begin));
                }

                if (failed)
                {
                    await Task.Delay(FailureBackoff);
                }
                else if (thinkMilliseconds > 0)
                {
                    await Task.Delay(thinkMilliseconds);
                }
            }

            return samples;
        })).ToArray();

        var results = await Task.WhenAll(workers);
        var seconds = Math.Max(0.001, (Stopwatch.GetTimestamp() - measureFrom) / (double)Stopwatch.Frequency);
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
                var failed = false;
                int status;
                try
                {
                    using var response = await client.SendAsync(request);
                    status = (int)response.StatusCode;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    status = 0;
                    failed = true;
                    RecordError(exception);
                }

                samples.Add(new Sample(status, Stopwatch.GetTimestamp() - begin));

                if (failed)
                {
                    await Task.Delay(FailureBackoff);
                }
            }

            return samples;
        })).ToArray();

        var results = await Task.WhenAll(workers);
        var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        return new RunResult(scenario.Name, seconds, results.SelectMany(list => list).ToList());
    }
}

using System.Diagnostics;

namespace GatewayForge.LoadTest;

internal static class Report
{
    public static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static double Percentile(IEnumerable<Sample> samples, double fraction)
    {
        var sorted = samples.Select(sample => sample.Ticks).Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1);
        return ToMilliseconds(sorted[index]);
    }

    public static void PrintComparison(IReadOnlyList<RunResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"scenario",-26}{"requests",10}{"req/s",11}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}{"non-2xx",9}");
        Console.WriteLine(new string('-', 83));

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{result.Name,-26}{result.Total,10:N0}{result.RequestsPerSecond,11:N0}" +
                $"{Percentile(result.Samples, 0.50),9:F2}{Percentile(result.Samples, 0.95),9:F2}" +
                $"{Percentile(result.Samples, 0.99),9:F2}{result.NonSuccess,9:N0}");
        }

        var baseline = results[0];
        var baselineMedian = Percentile(baseline.Samples, 0.50);
        Console.WriteLine();
        Console.WriteLine($"Baseline: {baseline.Name} ({baseline.RequestsPerSecond:N0} req/s, p50 {baselineMedian:F2} ms)");

        foreach (var result in results.Skip(1))
        {
            var median = Percentile(result.Samples, 0.50);
            var ratio = result.RequestsPerSecond / baseline.RequestsPerSecond;
            var direction = ratio >= 1 ? "faster" : "slower";
            var factor = ratio >= 1 ? ratio : 1 / ratio;
            Console.WriteLine(
                $"  {result.Name}: {factor:F2}x {direction}, {100 * (ratio - 1):+0;-0}% throughput, " +
                $"{median - baselineMedian:+0.00;-0.00} ms median latency");
        }

        var throttled = results.Sum(result => result.NonSuccess);
        if (throttled > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"WARNING: {throttled:N0} non-2xx responses. Results are skewed; start the stack with " +
                "docker-compose.benchmark.yml so the rate limiter does not throttle the test.");
        }
    }

    public static void PrintFlood(RunResult result)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Sent {result.Total:N0} requests in {result.Seconds:F1}s ({result.RequestsPerSecond:N0} req/s) at one user.");
        Console.WriteLine();
        Console.WriteLine($"{"status",-10}{"count",10}{"share",9}{"p50 ms",9}{"p95 ms",9}");
        Console.WriteLine(new string('-', 47));

        foreach (var group in result.Samples.GroupBy(sample => sample.Status).OrderBy(group => group.Key))
        {
            var label = group.Key switch
            {
                0 => "error",
                200 => "200 OK",
                429 => "429 limited",
                var other => other.ToString()
            };

            Console.WriteLine(
                $"{label,-10}{group.Count(),10:N0}{100.0 * group.Count() / result.Total,8:F1}%" +
                $"{Percentile(group, 0.50),9:F2}{Percentile(group, 0.95),9:F2}");
        }
    }
}

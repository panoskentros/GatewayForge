namespace GatewayForge.LoadTest;

internal sealed record Phase(string Name, int Users, List<UserSample> Samples, double Seconds)
{
    public double RequestsPerSecond => Samples.Count / Seconds;
    public int NonSuccess => Samples.Count(sample => sample.Status is < 200 or >= 300);
}

internal static class UserReport
{
    private static double Percentile(IEnumerable<UserSample> samples, double fraction)
    {
        var sorted = samples.Select(sample => sample.Ticks).Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1);
        return Report.ToMilliseconds(sorted[index]);
    }

    public static void PrintPhases(IReadOnlyList<Phase> phases)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"{"scenario",-26}{"users",6}{"requests",10}{"req/s",9}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}" +
            $"{"non-2xx",9}{"per-user min/max",19}");
        Console.WriteLine(new string('-', 106));

        foreach (var phase in phases)
        {
            var perUser = phase.Samples.GroupBy(sample => sample.User).Select(group => group.Count()).ToArray();
            Console.WriteLine(
                $"{phase.Name,-26}{phase.Users,6}{phase.Samples.Count,10:N0}{phase.RequestsPerSecond,9:N0}" +
                $"{Percentile(phase.Samples, 0.50),9:F2}{Percentile(phase.Samples, 0.95),9:F2}" +
                $"{Percentile(phase.Samples, 0.99),9:F2}{phase.NonSuccess,9:N0}" +
                $"{perUser.Min() + " / " + perUser.Max(),19}");
        }

        var baseline = phases[0];
        var baselineMedian = Percentile(baseline.Samples, 0.50);
        Console.WriteLine();
        foreach (var phase in phases.Skip(1))
        {
            var median = Percentile(phase.Samples, 0.50);
            var ratio = phase.RequestsPerSecond / baseline.RequestsPerSecond;
            var direction = ratio >= 1 ? "faster" : "slower";
            var factor = ratio >= 1 ? ratio : 1 / ratio;
            Console.WriteLine(
                $"{phase.Name} is {factor:F2}x {direction} than {baseline.Name}: " +
                $"{100 * (ratio - 1):+0;-0}% throughput, {median - baselineMedian:+0.00;-0.00} ms median latency");
        }

        var throttled = phases.Sum(phase => phase.NonSuccess);
        if (throttled > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"WARNING: {throttled:N0} non-2xx responses; results are skewed. Use docker-compose.benchmark.yml.");
        }
    }

    public static void PrintNoisyNeighbour(IReadOnlyList<UserSample> samples, double seconds, int abuser)
    {
        var normal = samples.Where(sample => sample.User != abuser).ToList();
        var abuserSamples = samples.Where(sample => sample.User == abuser).ToList();
        var normalUsers = normal.Select(sample => sample.User).Distinct().Count();

        Console.WriteLine();
        Console.WriteLine(
            $"{normalUsers} normal users (about 1 request every 1.2s each) and 1 abusive user, {seconds:F0}s.");
        Console.WriteLine();
        Console.WriteLine("Normal users, by route");
        Console.WriteLine($"{"route",-26}{"requests",10}{"2xx",9}{"429",7}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}");
        Console.WriteLine(new string('-', 79));

        foreach (var group in normal.GroupBy(sample => sample.Route).OrderBy(group => group.Key))
        {
            var ok = group.Count(sample => sample.Status is >= 200 and < 300);
            var limited = group.Count(sample => sample.Status == 429);
            Console.WriteLine(
                $"{group.Key,-26}{group.Count(),10:N0}{ok,9:N0}{limited,7:N0}" +
                $"{Percentile(group, 0.50),9:F2}{Percentile(group, 0.95),9:F2}{Percentile(group, 0.99),9:F2}");
        }

        var abuserOk = abuserSamples.Count(sample => sample.Status is >= 200 and < 300);
        var abuserLimited = abuserSamples.Count(sample => sample.Status == 429);
        Console.WriteLine();
        Console.WriteLine("Abusive user (no pauses, internal route)");
        Console.WriteLine(
            $"  sent {abuserSamples.Count:N0} requests ({abuserSamples.Count / seconds:N0} req/s): " +
            $"{abuserOk:N0} allowed, {abuserLimited:N0} rejected with 429 " +
            $"({100.0 * abuserLimited / Math.Max(1, abuserSamples.Count):F2}%)");

        var normalLimited = normal.Count(sample => sample.Status == 429);
        var normalFailed = normal.Count(sample => sample.Status is not (>= 200 and < 300) and not 429);
        Console.WriteLine();
        Console.WriteLine(
            normalLimited == 0 && normalFailed == 0
                ? "Result: no normal user was throttled or failed while the abuser was being rejected."
                : $"Result: {normalLimited:N0} normal requests throttled, {normalFailed:N0} failed.");
    }
}

using System.Diagnostics;
using System.Net.Http.Headers;

namespace GatewayForge.LoadTest;

internal sealed record Route(string Name, string Url, bool Authenticated);

internal readonly record struct UserSample(int User, string Route, int Status, long Ticks);

internal static class UserLoad
{
    public static async Task<(List<UserSample> Samples, double Seconds)> RunAsync(
        HttpClient client,
        IReadOnlyList<VirtualUser> users,
        Func<int, Random, Route> pickRoute,
        Func<int, Random, int> thinkMilliseconds,
        TimeSpan duration)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = started + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        var tasks = users.Select(user => Task.Run(async () =>
        {
            var random = new Random(user.Index * 7919);
            var samples = new List<UserSample>(1024);

            while (Stopwatch.GetTimestamp() < deadline)
            {
                var route = pickRoute(user.Index, random);
                using var request = new HttpRequestMessage(HttpMethod.Get, route.Url);
                if (route.Authenticated)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
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

                samples.Add(new UserSample(user.Index, route.Name, status, Stopwatch.GetTimestamp() - begin));

                var think = thinkMilliseconds(user.Index, random);
                if (think > 0)
                {
                    await Task.Delay(think);
                }
            }

            return samples;
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        return (results.SelectMany(list => list).ToList(), seconds);
    }
}

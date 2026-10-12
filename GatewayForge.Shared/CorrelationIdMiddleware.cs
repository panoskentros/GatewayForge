using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GatewayForge.Shared;

public static class CorrelationIdExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}

internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[GatewayHeaders.CorrelationId].FirstOrDefault();
        var correlationId = IsAcceptable(incoming) ? incoming! : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Request.Headers[GatewayHeaders.CorrelationId] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[GatewayHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope("{CorrelationId}", correlationId))
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await next(context);
            }
            finally
            {
                var level = context.Request.Path.StartsWithSegments("/health")
                    ? LogLevel.Debug
                    : LogLevel.Information;

                logger.Log(level, "{Method} {Path} responded {StatusCode} in {ElapsedMs} ms",
                    context.Request.Method, context.Request.Path.Value,
                    context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private static bool IsAcceptable(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

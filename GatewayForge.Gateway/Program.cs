using System.Threading.RateLimiting;
using GatewayForge.Shared;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var sharedKey = builder.Configuration["Gateway:SharedKey"];
if (string.IsNullOrWhiteSpace(sharedKey))
{
    throw new InvalidOperationException(
        "Configuration value 'Gateway:SharedKey' is required (set GATEWAY_SHARED_KEY in .env).");
}

builder.Services.AddGatewayForgeJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GatewayPolicies.SecureAccess, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(GatewayRoles.SecureUser))
    .AddPolicy(GatewayPolicies.InternalAccess, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(GatewayRoles.InternalUser));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-user", httpContext =>
    {
        var settings = httpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimiting");
        var partitionKey = GatewayTrust.GetUserId(httpContext.User)
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.GetValue("PermitLimit", 60),
            Window = TimeSpan.FromSeconds(settings.GetValue("WindowSeconds", 60)),
            QueueLimit = 0
        });
    });
});

builder.Services.AddHealthChecks();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transform =>
    {
        var isEdgeOffloaded = transform.Route.RouteId == "edge-offloaded-route";

        transform.AddRequestTransform(context =>
        {
            if (isEdgeOffloaded)
            {
                GatewayTrust.Stamp(
                    context.ProxyRequest.Headers,
                    GatewayTrust.GetUserId(context.HttpContext.User),
                    sharedKey);
            }
            else
            {
                GatewayTrust.RemoveClientSuppliedHeaders(context.ProxyRequest.Headers);
            }

            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

app.UseCorrelationId();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }

using GatewayForge.Shared;

var builder = WebApplication.CreateBuilder(args);

var sharedKey = builder.Configuration["Gateway:SharedKey"];
if (string.IsNullOrWhiteSpace(sharedKey))
{
    throw new InvalidOperationException(
        "Configuration value 'Gateway:SharedKey' is required (set GATEWAY_SHARED_KEY in .env).");
}

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCorrelationId();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    if (!GatewayTrust.IsFromGateway(context.HttpContext.Request, sharedKey))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Requests must come through the gateway.");
    }

    return await next(context);
});

api.MapGet("/internal-data", (HttpContext context) =>
{
    var userId = context.Request.Headers[GatewayHeaders.UserId].FirstOrDefault() ?? "Anonymous";
    return Results.Ok(new { message = "Hello from Internal Service (Edge-Offloaded)", userId });
});

app.Run();

public partial class Program { }

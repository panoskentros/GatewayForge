using GatewayForge.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGatewayForgeJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GatewayPolicies.SecureAccess, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(GatewayRoles.SecureUser));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCorrelationId();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGet("/api/secure-data", (HttpContext context) =>
{
    var user = context.User.Identity?.Name ?? "Unknown";
    return Results.Ok(new { message = "Hello from Secure Service (Zero-Trust Token Relay)", user });
}).RequireAuthorization(GatewayPolicies.SecureAccess);

app.Run();

public partial class Program { }

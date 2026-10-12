var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5002");

var app = builder.Build();

app.MapGet("/api/internal-data", (HttpContext context) =>
{
    var userId = context.Request.Headers["X-User-Id"].FirstOrDefault() ?? "Anonymous";
    return Results.Ok(new { message = "Hello from Internal Service (Edge-Offloaded)", userId });
});

app.Run();
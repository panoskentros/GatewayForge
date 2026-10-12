using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5001");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://localhost:8080/realms/gateway-forge";
        options.Audience = "account";
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username"
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/secure-data", (HttpContext context) =>
{
    var user = context.User.Identity?.Name ?? "Unknown";
    return Results.Ok(new { message = "Hello from Secure Service (Zero-Trust Token Relay)", user });
});

app.Run();
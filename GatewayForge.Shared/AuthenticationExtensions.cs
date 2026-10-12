using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GatewayForge.Shared;

public sealed class GatewayAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;
}

public static class AuthenticationExtensions
{
    public static AuthenticationBuilder AddGatewayForgeJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(GatewayAuthenticationOptions.SectionName)
            .Get<GatewayAuthenticationOptions>() ?? new GatewayAuthenticationOptions();

        if (string.IsNullOrWhiteSpace(settings.Authority))
        {
            throw new InvalidOperationException("Configuration value 'Authentication:Authority' is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException("Configuration value 'Authentication:Audience' is required.");
        }

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    NameClaimType = "preferred_username",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            KeycloakRoleMapper.AddRoleClaims(identity);
                        }

                        return Task.CompletedTask;
                    }
                };
            });
    }
}

public static class KeycloakRoleMapper
{
    public static void AddRoleClaims(ClaimsIdentity identity)
    {
        var raw = identity.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("roles", out var roles) ||
                roles.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var role in roles.EnumerateArray())
            {
                var name = role.ValueKind == JsonValueKind.String ? role.GetString() : null;
                if (!string.IsNullOrEmpty(name) && !identity.HasClaim(ClaimTypes.Role, name))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, name));
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}

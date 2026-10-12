using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace GatewayForge.Tests;

internal static class TestHelpers
{
    public const string SharedKey = "test-shared-key-0123456789";

    public static void EnsureEnvironment()
    {
        Environment.SetEnvironmentVariable("Gateway__SharedKey", SharedKey);
    }

    public static void UseTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
    }
}

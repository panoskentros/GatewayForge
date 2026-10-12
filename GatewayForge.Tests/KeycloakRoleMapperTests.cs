using System.Security.Claims;
using GatewayForge.Shared;

namespace GatewayForge.Tests;

public class KeycloakRoleMapperTests
{
    private static ClaimsIdentity IdentityWith(string? realmAccessJson)
    {
        var identity = new ClaimsIdentity("test");
        if (realmAccessJson is not null)
        {
            identity.AddClaim(new Claim("realm_access", realmAccessJson, "JSON"));
        }

        return identity;
    }

    private static string[] Roles(ClaimsIdentity identity) =>
        identity.FindAll(ClaimTypes.Role).Select(c => c.Value).Order().ToArray();

    [Fact]
    public void Flattens_realm_access_roles_into_role_claims()
    {
        var identity = IdentityWith("""{"roles":["secure-user","internal-user"]}""");

        KeycloakRoleMapper.AddRoleClaims(identity);

        Assert.Equal(new[] { "internal-user", "secure-user" }, Roles(identity));
    }

    [Fact]
    public void Is_idempotent()
    {
        var identity = IdentityWith("""{"roles":["secure-user"]}""");

        KeycloakRoleMapper.AddRoleClaims(identity);
        KeycloakRoleMapper.AddRoleClaims(identity);

        Assert.Equal(new[] { "secure-user" }, Roles(identity));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"roles":"secure-user"}""")]
    [InlineData("""{"other":[]}""")]
    [InlineData("""{"roles":[1,null,{}]}""")]
    public void Fails_closed_on_missing_or_malformed_claims(string? json)
    {
        var identity = IdentityWith(json);

        KeycloakRoleMapper.AddRoleClaims(identity);

        Assert.Empty(Roles(identity));
    }
}

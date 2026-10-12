namespace GatewayForge.Shared;

public static class GatewayHeaders
{
    public const string UserId = "X-User-Id";
    public const string GatewayKey = "X-Gateway-Key";
    public const string CorrelationId = "X-Correlation-Id";
}

public static class GatewayRoles
{
    public const string SecureUser = "secure-user";
    public const string InternalUser = "internal-user";
}

public static class GatewayPolicies
{
    public const string SecureAccess = "SecureAccess";
    public const string InternalAccess = "InternalAccess";
}

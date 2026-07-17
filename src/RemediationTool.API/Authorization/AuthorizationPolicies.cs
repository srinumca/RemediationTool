using System.Security.Claims;

namespace RemediationTool.API.Authorization;

/// <summary>
/// Named authorization policies used by the retained API controllers.
/// </summary>
public static class AuthorizationPolicies
{
    public const string AdminAccess = "Remediation.AdminAccess";
    public const string InternalApplication = "Remediation.InternalApplication";
}

/// <summary>
/// Default Entra app-role values used by the retained user-facing API.
/// </summary>
public static class RemediationRoleDefaults
{
    public const string SystemAdmin = "System_Admin";
    public const string Admin = "Admin";
}

/// <summary>
/// Claim checks shared by authorization policies.
/// </summary>
public static class AuthorizationClaimChecks
{
    private static readonly string[] ScopeClaimTypes =
    [
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    ];

    private static readonly string[] RoleClaimTypes =
    [
        "roles",
        ClaimTypes.Role
    ];

    public static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(requiredScope))
            return false;

        return user.Claims
            .Where(claim => ScopeClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .SelectMany(claim => SplitClaimValues(claim.Value))
            .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasRole(ClaimsPrincipal user, string requiredRole)
    {
        if (string.IsNullOrWhiteSpace(requiredRole))
            return false;

        if (user.IsInRole(requiredRole))
            return true;

        return user.Claims
            .Where(claim => RoleClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .SelectMany(claim => SplitClaimValues(claim.Value))
            .Contains(requiredRole, StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasAnyRole(ClaimsPrincipal user, params string[] requiredRoles)
        => requiredRoles.Any(role => HasRole(user, role));

    private static IEnumerable<string> SplitClaimValues(string value)
    {
        return value.Split(
            [' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

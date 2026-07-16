using System.Security.Claims;

namespace RemediationTool.API.Authorization;

/// <summary>
/// Named authorization policies used by API controllers.
/// Keeping policy names in one place avoids string duplication and makes
/// future Entra role-name changes configuration-only.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ReadAccess = "Remediation.ReadAccess";
    public const string AdminAccess = "Remediation.AdminAccess";
    public const string SystemAdminAccess = "Remediation.SystemAdminAccess";
    public const string InternalApplication = "Remediation.InternalApplication";
}

/// <summary>
/// Default Entra app-role values. These values can be overridden through
/// Authorization:Roles configuration without changing controller code.
/// </summary>
public static class RemediationRoleDefaults
{
    public const string SystemAdmin = "System_Admin";
    public const string Admin = "Admin";
    public const string User = "User";
    public const string ViewOnly = "View_Only";
}

/// <summary>
/// Claim checks shared by authorization policies.
/// Microsoft Entra can emit scopes and roles using different claim type names
/// depending on token version and claim mapping, so both standard forms are accepted.
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
    {
        return requiredRoles.Any(role => HasRole(user, role));
    }

    private static IEnumerable<string> SplitClaimValues(string value)
    {
        return value.Split(
            [' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

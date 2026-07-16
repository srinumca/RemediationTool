using System.Security.Claims;
using RemediationTool.API.Authorization;

namespace RemediationTool.API.Tests;

public class AuthorizationClaimChecksTests
{
    [Fact]
    public void HasScope_ReturnsTrue_WhenDelegatedScopeIsPresent()
    {
        var user = CreateUser(new Claim("scp", "openid profile access_as_user"));

        var result = AuthorizationClaimChecks.HasScope(user, "access_as_user");

        Assert.True(result);
    }

    [Fact]
    public void HasScope_ReturnsFalse_WhenDelegatedScopeIsMissing()
    {
        var user = CreateUser(new Claim("scp", "openid profile"));

        var result = AuthorizationClaimChecks.HasScope(user, "access_as_user");

        Assert.False(result);
    }

    [Fact]
    public void HasRole_ReturnsTrue_WhenBusinessRoleIsPresent()
    {
        var user = CreateUser(new Claim("roles", "Admin"));

        var result = AuthorizationClaimChecks.HasRole(user, "Admin");

        Assert.True(result);
    }

    [Fact]
    public void HasRole_ReturnsTrue_WhenApplicationRoleIsPresent()
    {
        var user = CreateUser(new Claim("roles", "access_as_application"));

        var result = AuthorizationClaimChecks.HasRole(user, "access_as_application");

        Assert.True(result);
    }

    [Fact]
    public void HasAnyRole_ReturnsTrue_WhenOneConfiguredRoleMatches()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "View_Only"));

        var result = AuthorizationClaimChecks.HasAnyRole(
            user,
            "System_Admin",
            "Admin",
            "User",
            "View_Only");

        Assert.True(result);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}

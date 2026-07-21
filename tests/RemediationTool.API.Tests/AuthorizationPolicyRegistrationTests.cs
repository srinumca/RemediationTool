using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RemediationTool.API.Authorization;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class AuthorizationPolicyRegistrationTests
{
    [Theory]
    [InlineData(RemediationRoleDefaults.SystemAdmin)]
    [InlineData(RemediationRoleDefaults.Admin)]
    public async Task AdminAccess_Enabled_AllowsConfiguredDefaultRoles(string role)
    {
        await using var provider = BuildProvider(authenticationEnabled: true);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = AuthenticatedUser(new Claim("roles", role));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            AuthorizationPolicies.AdminAccess);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AdminAccess_Enabled_DeniesAuthenticatedUserWithoutRequiredRole()
    {
        await using var provider = BuildProvider(authenticationEnabled: true);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = AuthenticatedUser(new Claim("roles", "Reader"));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            AuthorizationPolicies.AdminAccess);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AdminAccess_Enabled_UsesCustomConfiguredRoles()
    {
        await using var provider = BuildProvider(
            authenticationEnabled: true,
            new Dictionary<string, string?>
            {
                ["Authorization:Roles:SystemAdmin"] = "PlatformOwner",
                ["Authorization:Roles:Admin"] = "DataAdmin"
            });
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var customRoleResult = await authorization.AuthorizeAsync(
            AuthenticatedUser(new Claim(ClaimTypes.Role, "dataadmin")),
            resource: null,
            AuthorizationPolicies.AdminAccess);
        var defaultRoleResult = await authorization.AuthorizeAsync(
            AuthenticatedUser(new Claim("roles", RemediationRoleDefaults.Admin)),
            resource: null,
            AuthorizationPolicies.AdminAccess);

        Assert.True(customRoleResult.Succeeded);
        Assert.False(defaultRoleResult.Succeeded);
    }

    [Fact]
    public async Task InternalApplication_Enabled_RequiresAuthenticatedIdentity()
    {
        await using var provider = BuildProvider(authenticationEnabled: true);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var authenticated = await authorization.AuthorizeAsync(
            AuthenticatedUser(),
            resource: null,
            AuthorizationPolicies.InternalApplication);
        var anonymous = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            AuthorizationPolicies.InternalApplication);

        Assert.True(authenticated.Succeeded);
        Assert.False(anonymous.Succeeded);
    }

    [Theory]
    [InlineData(AuthorizationPolicies.AdminAccess)]
    [InlineData(AuthorizationPolicies.InternalApplication)]
    public async Task AuthenticationDisabled_AllowsAnonymousLocalAccess(string policy)
    {
        await using var provider = BuildProvider(authenticationEnabled: false);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AddRemediationAuthorizationPolicies_ReturnsOriginalServiceCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var returned = services.AddRemediationAuthorizationPolicies(
            configuration,
            authenticationEnabled: false);

        Assert.Same(services, returned);
    }

    private static ServiceProvider BuildProvider(
        bool authenticationEnabled,
        IDictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddRemediationAuthorizationPolicies(configuration, authenticationEnabled);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}

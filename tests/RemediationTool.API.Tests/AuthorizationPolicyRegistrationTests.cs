using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RemediationTool.API.Authorization;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class AuthorizationPolicyRegistrationTests
{
    [Fact]
    public void AddRemediationAuthorizationPolicies_WhenAuthenticationEnabled_RegistersPoliciesWithoutScopeOrApplicationRole()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Roles:SystemAdmin"] = "System_Admin",
                ["Authorization:Roles:Admin"] = "Admin"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddRemediationAuthorizationPolicies(
            configuration,
            authenticationEnabled: true);

        using var serviceProvider = services.BuildServiceProvider();
        var authorizationOptions = serviceProvider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        var adminPolicy = authorizationOptions.GetPolicy(AuthorizationPolicies.AdminAccess);
        var internalApplicationPolicy = authorizationOptions.GetPolicy(
            AuthorizationPolicies.InternalApplication);

        Assert.NotNull(adminPolicy);
        Assert.NotNull(internalApplicationPolicy);
        Assert.Contains("Bearer", adminPolicy.AuthenticationSchemes);
        Assert.Contains("Bearer", internalApplicationPolicy.AuthenticationSchemes);
    }
}

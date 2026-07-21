using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace RemediationTool.API.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddRemediationAuthorizationPolicies(
        this IServiceCollection services,
        IConfiguration configuration,
        bool authenticationEnabled)
    {
        var systemAdminRole = configuration["Authorization:Roles:SystemAdmin"]
            ?? RemediationRoleDefaults.SystemAdmin;
        var adminRole = configuration["Authorization:Roles:Admin"]
            ?? RemediationRoleDefaults.Admin;

        services.AddAuthorization(options =>
        {
            if (!authenticationEnabled)
            {
                // Keep local environments usable until Entra registrations are supplied.
                AddDisabledPolicy(options, AuthorizationPolicies.AdminAccess);
                AddDisabledPolicy(options, AuthorizationPolicies.InternalApplication);
                return;
            }

            AddRolePolicy(
                options,
                AuthorizationPolicies.AdminAccess,
                systemAdminRole,
                adminRole);

            options.AddPolicy(
                AuthorizationPolicies.InternalApplication,
                policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser());
        });

        return services;
    }

    private static void AddRolePolicy(
        AuthorizationOptions options,
        string policyName,
        params string[] allowedRoles)
    {
        options.AddPolicy(
            policyName,
            policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    AuthorizationClaimChecks.HasAnyRole(context.User, allowedRoles)));
    }

    private static void AddDisabledPolicy(
        AuthorizationOptions options,
        string policyName)
    {
        options.AddPolicy(policyName, policy => policy.RequireAssertion(_ => true));
    }
}

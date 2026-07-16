using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace RemediationTool.API.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddRemediationAuthorizationPolicies(
        this IServiceCollection services,
        IConfiguration configuration,
        bool authenticationEnabled,
        string delegatedScope,
        string applicationRole)
    {
        var systemAdminRole = configuration["Authorization:Roles:SystemAdmin"]
            ?? RemediationRoleDefaults.SystemAdmin;
        var adminRole = configuration["Authorization:Roles:Admin"]
            ?? RemediationRoleDefaults.Admin;
        var userRole = configuration["Authorization:Roles:User"]
            ?? RemediationRoleDefaults.User;
        var viewOnlyRole = configuration["Authorization:Roles:ViewOnly"]
            ?? RemediationRoleDefaults.ViewOnly;

        services.AddAuthorization(options =>
        {
            if (!authenticationEnabled)
            {
                // Authentication is intentionally disabled until Entra registrations
                // are supplied. Register permissive versions so policy-decorated
                // controllers continue to work in the existing local environment.
                AddDisabledPolicy(options, AuthorizationPolicies.ReadAccess);
                AddDisabledPolicy(options, AuthorizationPolicies.AdminAccess);
                AddDisabledPolicy(options, AuthorizationPolicies.SystemAdminAccess);
                AddDisabledPolicy(options, AuthorizationPolicies.InternalApplication);
                return;
            }

            AddDelegatedRolePolicy(
                options,
                AuthorizationPolicies.ReadAccess,
                delegatedScope,
                systemAdminRole,
                adminRole,
                userRole,
                viewOnlyRole);

            AddDelegatedRolePolicy(
                options,
                AuthorizationPolicies.AdminAccess,
                delegatedScope,
                systemAdminRole,
                adminRole);

            AddDelegatedRolePolicy(
                options,
                AuthorizationPolicies.SystemAdminAccess,
                delegatedScope,
                systemAdminRole);

            options.AddPolicy(
                AuthorizationPolicies.InternalApplication,
                policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        AuthorizationClaimChecks.HasRole(context.User, applicationRole)));
        });

        return services;
    }

    private static void AddDelegatedRolePolicy(
        AuthorizationOptions options,
        string policyName,
        string delegatedScope,
        params string[] allowedRoles)
    {
        options.AddPolicy(
            policyName,
            policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    AuthorizationClaimChecks.HasScope(context.User, delegatedScope)
                    && AuthorizationClaimChecks.HasAnyRole(context.User, allowedRoles)));
    }

    private static void AddDisabledPolicy(
        AuthorizationOptions options,
        string policyName)
    {
        options.AddPolicy(policyName, policy => policy.RequireAssertion(_ => true));
    }
}

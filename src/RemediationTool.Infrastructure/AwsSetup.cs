using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RemediationTool.Infrastructure;

/// <summary>
/// Registers IAmazonDynamoDB and IAmazonS3 clients with full credential support:
///
///   Priority 1 — Explicit keys in appsettings (permanent IAM or temporary SSO keys pasted manually)
///                AWS:AccessKey + AWS:SecretKey + optionally AWS:SessionToken
///
///   Priority 2 — Named profile from ~/.aws/credentials
///                AWS:Profile = "saml"  (matches what aws-generate-sso-cred.py writes)
///
///   Priority 3 — SDK default credential chain
///                Reads [default] profile from ~/.aws/credentials automatically
///
/// Clients are registered via factory lambdas so credentials are resolved LAZILY
/// (at first call, not at startup). The app starts successfully without credentials
/// and fails gracefully at the endpoint level — same pattern as AwsSampleApi.
/// </summary>
public static class AwsSetup
{
    public static IServiceCollection AddRemediationAwsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var awsOptions = configuration.GetAWSOptions();

        // --- Credential resolution (mirrors AwsSampleApi/Infrastructure/AwsSetup.cs) ---

        var profileName = configuration["AWS:Profile"];
        var sharedCredentialsFile = configuration["AWS:SharedCredentialsFile"];
        var accessKey = configuration["AWS:AccessKey"];
        var secretKey = configuration["AWS:SecretKey"];
        var sessionToken = configuration["AWS:SessionToken"];

        // If a named profile is specified, tell the SDK via environment variable.
        // This makes the SSO-generated credentials file work when the profile
        // name is not "default" (e.g. "saml", "AWS-PowerUsers", etc.)
        if (!string.IsNullOrWhiteSpace(profileName))
            Environment.SetEnvironmentVariable("AWS_PROFILE", profileName);

        if (!string.IsNullOrWhiteSpace(sharedCredentialsFile))
            Environment.SetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE", sharedCredentialsFile);

        // Build explicit credentials only when keys are present in config.
        // When keys are absent, pass null and let the SDK read ~/.aws/credentials.
        AWSCredentials? explicitCreds = null;

        if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
        {
            explicitCreds = string.IsNullOrWhiteSpace(sessionToken)
                ? new BasicAWSCredentials(accessKey, secretKey)
                // SessionAWSCredentials is required for SSO/STS temporary tokens
                : new SessionAWSCredentials(accessKey, secretKey, sessionToken);
        }

        var region = awsOptions.Region ?? Amazon.RegionEndpoint.APSouth1;

        // --- IAmazonDynamoDB (lazy factory — app starts without credentials) ---
        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var config = new AmazonDynamoDBConfig { RegionEndpoint = region };
            return explicitCreds is not null
                ? new AmazonDynamoDBClient(explicitCreds, config)
                : new AmazonDynamoDBClient(config);
        });

        // --- IAmazonS3 (lazy factory) ---
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new Amazon.S3.AmazonS3Config { RegionEndpoint = region };
            return explicitCreds is not null
                ? new AmazonS3Client(explicitCreds, config)
                : new AmazonS3Client(config);
        });

        return services;
    }
}
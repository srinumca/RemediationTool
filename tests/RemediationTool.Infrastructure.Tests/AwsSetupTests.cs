using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RemediationTool.Infrastructure;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

[CollectionDefinition("AWS environment variables", DisableParallelization = true)]
public sealed class AwsEnvironmentCollection
{
    public const string Name = "AWS environment variables";
}

[Collection(AwsEnvironmentCollection.Name)]
public sealed class AwsSetupTests : IDisposable
{
    private readonly string? _originalProfile =
        Environment.GetEnvironmentVariable("AWS_PROFILE");
    private readonly string? _originalCredentialsFile =
        Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE");

    [Fact]
    public void AddRemediationAwsServices_ReturnsSameCollectionAndRegistersSingletonClients()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(
            ("AWS:Region", "us-east-2"),
            ("AWS:AccessKey", "test-access-key"),
            ("AWS:SecretKey", "test-secret-key"));

        var returned = services.AddRemediationAwsServices(configuration);
        using var provider = services.BuildServiceProvider();
        var dynamo1 = provider.GetRequiredService<IAmazonDynamoDB>();
        var dynamo2 = provider.GetRequiredService<IAmazonDynamoDB>();
        var s3First = provider.GetRequiredService<IAmazonS3>();
        var s3Second = provider.GetRequiredService<IAmazonS3>();

        Assert.Same(services, returned);
        Assert.Same(dynamo1, dynamo2);
        Assert.Same(s3First, s3Second);
        Assert.IsType<AmazonDynamoDBClient>(dynamo1);
        Assert.IsType<AmazonS3Client>(s3First);
    }

    [Fact]
    public void AddRemediationAwsServices_SessionCredentials_ResolveClientsLazily()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(
            ("AWS:Region", "us-west-2"),
            ("AWS:AccessKey", "temporary-key"),
            ("AWS:SecretKey", "temporary-secret"),
            ("AWS:SessionToken", "temporary-token"));

        services.AddRemediationAwsServices(configuration);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(AmazonDynamoDBClient)
                || descriptor.ServiceType == typeof(AmazonS3Client));

        using var provider = services.BuildServiceProvider();
        Assert.IsAssignableFrom<IAmazonDynamoDB>(
            provider.GetRequiredService<IAmazonDynamoDB>());
        Assert.IsAssignableFrom<IAmazonS3>(
            provider.GetRequiredService<IAmazonS3>());
    }

    [Fact]
    public void AddRemediationAwsServices_ProfileSettings_UpdateSdkEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("AWS_PROFILE", null);
        Environment.SetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE", null);
        var credentialsFile = Path.Combine(
            Path.GetTempPath(),
            $"credentials-{Guid.NewGuid():N}");
        var configuration = Configuration(
            ("AWS:Profile", "saml-profile"),
            ("AWS:SharedCredentialsFile", credentialsFile),
            ("AWS:AccessKey", "key"),
            ("AWS:SecretKey", "secret"));

        new ServiceCollection().AddRemediationAwsServices(configuration);

        Assert.Equal(
            "saml-profile",
            Environment.GetEnvironmentVariable("AWS_PROFILE"));
        Assert.Equal(
            credentialsFile,
            Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE"));
    }

    [Fact]
    public void AddRemediationAwsServices_BlankProfileSettings_DoNotOverwriteExistingEnvironment()
    {
        Environment.SetEnvironmentVariable("AWS_PROFILE", "existing-profile");
        Environment.SetEnvironmentVariable(
            "AWS_SHARED_CREDENTIALS_FILE",
            "existing-file");
        var configuration = Configuration(
            ("AWS:Profile", " "),
            ("AWS:SharedCredentialsFile", " "),
            ("AWS:AccessKey", "key"),
            ("AWS:SecretKey", "secret"));

        new ServiceCollection().AddRemediationAwsServices(configuration);

        Assert.Equal(
            "existing-profile",
            Environment.GetEnvironmentVariable("AWS_PROFILE"));
        Assert.Equal(
            "existing-file",
            Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AWS_PROFILE", _originalProfile);
        Environment.SetEnvironmentVariable(
            "AWS_SHARED_CREDENTIALS_FILE",
            _originalCredentialsFile);
    }

    private static IConfiguration Configuration(
        params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(
                value => value.Key,
                value => value.Value,
                StringComparer.OrdinalIgnoreCase))
            .Build();
}

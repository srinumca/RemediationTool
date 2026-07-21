using Microsoft.Extensions.Configuration;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class TemporaryDirectoryFixture : IAsyncLifetime
{
    public string RootPath { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "remediation-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    Thread.Sleep(25);
                }
            }
        }

        return Task.CompletedTask;
    }

    public IConfiguration Configuration(params (string Key, string? Value)[] values)
    {
        var settings = values.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}

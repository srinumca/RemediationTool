using Microsoft.Extensions.Configuration;

namespace RemediationTool.Infrastructure.Tests;

public sealed class TemporaryDirectoryFixture : IDisposable
{
    private const int DeleteAttemptCount = 3;
    private const int DeleteRetryDelayMilliseconds = 25;

    public TemporaryDirectoryFixture()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "remediation-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

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

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
            return;

        for (var attempt = 1; attempt <= DeleteAttemptCount; attempt++)
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < DeleteAttemptCount)
            {
                Thread.Sleep(DeleteRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException) when (attempt < DeleteAttemptCount)
            {
                Thread.Sleep(DeleteRetryDelayMilliseconds);
            }
        }
    }
}

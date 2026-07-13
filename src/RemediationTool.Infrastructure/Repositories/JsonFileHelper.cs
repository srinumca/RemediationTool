namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Resilient file read/write helpers for JSON-backed repositories.
/// </summary>
public static class JsonFileHelper
{
    private const int BufferSize = 81920;

    public static string ReadAllText(string filePath, int maxAttempts = 5, int delayMs = 100)
    {
        return WithRetry(() =>
        {
            using var stream = new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    BufferSize = BufferSize,
                    Options = FileOptions.SequentialScan
                });
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }, maxAttempts, delayMs);
    }

    public static void WriteAllText(
        string filePath,
        string content,
        int maxAttempts = 5,
        int delayMs = 100)
    {
        WithRetry(() =>
        {
            using var stream = new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    BufferSize = BufferSize,
                    Options = FileOptions.SequentialScan
                });
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            return true;
        }, maxAttempts, delayMs);
    }

    private static T WithRetry<T>(Func<T> action, int maxAttempts, int delayMs)
    {
        ArgumentNullException.ThrowIfNull(action);

        var attempts = Math.Max(1, maxAttempts);
        var retryDelayMs = Math.Max(0, delayMs);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < attempts)
            {
                if (retryDelayMs > 0)
                    Thread.Sleep(retryDelayMs);
            }
        }
    }
}

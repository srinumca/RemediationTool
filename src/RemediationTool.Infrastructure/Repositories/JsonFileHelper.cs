namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Resilient file read/write helpers for the JSON-file-as-database repositories.
/// Uses FileShare.ReadWrite so transient locks (editors, Explorer previews, antivirus
/// scans, concurrent reads) don't cause IOExceptions, plus a short retry for the rare
/// case where a lock is briefly exclusive.
/// </summary>
public static class JsonFileHelper
{
    public static string ReadAllText(string filePath, int maxAttempts = 5, int delayMs = 100)
    {
        return WithRetry(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }, maxAttempts, delayMs);
    }

    public static void WriteAllText(string filePath, string content, int maxAttempts = 5, int delayMs = 100)
    {
        WithRetry(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            return true;
        }, maxAttempts, delayMs);
    }

    private static T WithRetry<T>(Func<T> action, int maxAttempts, int delayMs)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delayMs);
            }
        }

        return action(); // final attempt — let any exception propagate
    }
}
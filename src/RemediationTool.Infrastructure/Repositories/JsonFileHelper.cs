namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Resilient file read/write helpers for the JSON-file-as-database repositories.
/// Uses FileShare.ReadWrite so transient locks (editors, Explorer previews, antivirus
/// scans, concurrent reads) don't cause IOExceptions, plus a short retry for the rare
/// case where a lock is briefly exclusive.
/// </summary>
public static class JsonFileHelper
{
    /// <summary>
    /// Reads all text from a file with retry logic to handle transient IOExceptions.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="delayMs"></param>
    /// <returns></returns>
    public static string ReadAllText(string filePath, int maxAttempts = 5, int delayMs = 100)
    {
        return WithRetry(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }, maxAttempts, delayMs);
    }

    /// <summary>
    /// Writes all text to a file with retry logic to handle transient IOExceptions.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="content"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="delayMs"></param>
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

    /// <summary>
    /// Executes an action with retry logic to handle transient IOExceptions.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="action"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="delayMs"></param>
    /// <returns></returns>
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
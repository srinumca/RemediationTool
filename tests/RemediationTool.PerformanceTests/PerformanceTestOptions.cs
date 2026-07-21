namespace RemediationTool.PerformanceTests;

internal sealed record PerformanceTestOptions(
    Uri BaseUri,
    int RecordsPerJob,
    int Concurrency,
    int InvalidRowPercentage,
    string OutputDirectory,
    string? BearerToken,
    TimeSpan RequestTimeout,
    string ScenarioName)
{
    private const int MaximumConcurrency = 50;

    public static bool TryParse(
        string[] args,
        out PerformanceTestOptions? options,
        out string? error)
    {
        var values = ParseArguments(args);
        var baseUrl = ReadValue(values, "base-url", "REMEDIATION_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            options = null;
            error = "A valid HTTP or HTTPS --base-url value is required.";
            return false;
        }

        if (!TryReadPositiveInt(values, "records", "REMEDIATION_RECORDS", 10_000, out var records))
        {
            options = null;
            error = "--records must be a positive integer.";
            return false;
        }

        if (!TryReadPositiveInt(values, "concurrency", "REMEDIATION_CONCURRENCY", 1, out var concurrency)
            || concurrency > MaximumConcurrency)
        {
            options = null;
            error = $"--concurrency must be between 1 and {MaximumConcurrency}.";
            return false;
        }

        if (!TryReadInt(values, "invalid-percent", "REMEDIATION_INVALID_PERCENT", 0, out var invalidPercent)
            || invalidPercent is < 0 or > 100)
        {
            options = null;
            error = "--invalid-percent must be between 0 and 100.";
            return false;
        }

        if (!TryReadPositiveInt(values, "timeout-minutes", "REMEDIATION_TIMEOUT_MINUTES", 30, out var timeoutMinutes))
        {
            options = null;
            error = "--timeout-minutes must be a positive integer.";
            return false;
        }

        var outputDirectory = ReadValue(values, "output", "REMEDIATION_OUTPUT")
            ?? Path.Combine(
                "artifacts",
                "performance",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        var token = ReadValue(values, "token", "REMEDIATION_BEARER_TOKEN");
        var scenarioName = ReadValue(values, "scenario", "REMEDIATION_SCENARIO")
            ?? "ingestion-baseline";
        var normalizedBaseUri = new Uri(
            $"{baseUri.AbsoluteUri.TrimEnd('/')}/",
            UriKind.Absolute);

        options = new PerformanceTestOptions(
            normalizedBaseUri,
            records,
            concurrency,
            invalidPercent,
            outputDirectory,
            token,
            TimeSpan.FromMinutes(timeoutMinutes),
            scenarioName);
        error = null;
        return true;
    }

    public static string Usage => """
        Usage:
          dotnet run --project tests/RemediationTool.PerformanceTests -- \
            --base-url https://host \
            --records 10000 \
            --concurrency 1 \
            --invalid-percent 0 \
            --output artifacts/performance/run-001

        Optional environment variables:
          REMEDIATION_BASE_URL
          REMEDIATION_RECORDS
          REMEDIATION_CONCURRENCY
          REMEDIATION_INVALID_PERCENT
          REMEDIATION_OUTPUT
          REMEDIATION_BEARER_TOKEN
          REMEDIATION_TIMEOUT_MINUTES
          REMEDIATION_SCENARIO
        """;

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                continue;

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                values[argument[2..separatorIndex]] = argument[(separatorIndex + 1)..];
                continue;
            }

            var key = argument[2..];
            if (index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++index];
            }
            else
            {
                values[key] = "true";
            }
        }

        return values;
    }

    private static string? ReadValue(
        IReadOnlyDictionary<string, string> values,
        string argumentName,
        string environmentVariable)
    {
        if (values.TryGetValue(argumentName, out var argumentValue))
            return argumentValue;

        return Environment.GetEnvironmentVariable(environmentVariable);
    }

    private static bool TryReadPositiveInt(
        IReadOnlyDictionary<string, string> values,
        string argumentName,
        string environmentVariable,
        int defaultValue,
        out int result)
    {
        return TryReadInt(
                values,
                argumentName,
                environmentVariable,
                defaultValue,
                out result)
            && result > 0;
    }

    private static bool TryReadInt(
        IReadOnlyDictionary<string, string> values,
        string argumentName,
        string environmentVariable,
        int defaultValue,
        out int result)
    {
        var rawValue = ReadValue(values, argumentName, environmentVariable);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            result = defaultValue;
            return true;
        }

        return int.TryParse(rawValue, out result);
    }
}

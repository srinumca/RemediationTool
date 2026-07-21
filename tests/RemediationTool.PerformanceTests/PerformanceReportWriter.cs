using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemediationTool.PerformanceTests;

internal static class PerformanceReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(
        PerformanceTestResult result,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var jsonPath = Path.Combine(outputDirectory, "stress-results.json");
        var markdownPath = Path.Combine(outputDirectory, "STRESS_TEST_RESULTS.md");
        var htmlPath = Path.Combine(outputDirectory, "STRESS_TEST_RESULTS.html");

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            markdownPath,
            BuildMarkdown(result),
            cancellationToken);
        await File.WriteAllTextAsync(
            htmlPath,
            BuildHtml(result),
            cancellationToken);
    }

    private static string BuildMarkdown(PerformanceTestResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GFR Remediation API — Performance Test Results");
        builder.AppendLine();
        builder.AppendLine($"**Run ID:** `{result.TestRunId}`  ");
        builder.AppendLine($"**Scenario:** `{result.ScenarioName}`  ");
        builder.AppendLine($"**Environment:** `{result.EnvironmentName}`  ");
        builder.AppendLine($"**Base URL:** `{result.BaseUrl}`  ");
        builder.AppendLine($"**Git commit:** `{result.GitCommit}`  ");
        builder.AppendLine($"**Started:** `{result.StartedAtUtc:O}`  ");
        builder.AppendLine($"**Completed:** `{result.CompletedAtUtc:O}`");
        builder.AppendLine();
        builder.AppendLine("## Outcome");
        builder.AppendLine();
        builder.AppendLine($"**Result: {(result.Passed ? "PASS ✅" : "FAIL ❌")}**");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Jobs | {result.Jobs.Count:N0} |");
        builder.AppendLine($"| Requested concurrency | {result.RequestedConcurrency:N0} |");
        builder.AppendLine($"| Records per job | {result.RecordsPerJob:N0} |");
        builder.AppendLine($"| Total input records | {result.TotalInputRecords:N0} |");
        builder.AppendLine($"| Total processed records | {result.TotalProcessedRecords:N0} |");
        builder.AppendLine($"| Successful records | {result.TotalSucceededRecords:N0} |");
        builder.AppendLine($"| Rejected records | {result.TotalRejectedRecords:N0} |");
        builder.AppendLine($"| Failed jobs | {result.FailedJobCount:N0} |");
        builder.AppendLine($"| Persistence retries | {result.TotalRetryCount:N0} |");
        builder.AppendLine($"| Input data | {FormatBytes(result.TotalInputBytes)} |");
        builder.AppendLine($"| Wall time | {FormatDuration(result.WallTimeMilliseconds)} |");
        builder.AppendLine($"| Throughput | {result.RecordsPerSecond:N2} records/sec |");
        builder.AppendLine($"| Data throughput | {result.MegabytesPerSecond:N2} MB/sec |");
        builder.AppendLine($"| Rejection rate | {result.ErrorRatePercentage:N2}% |");
        builder.AppendLine();
        builder.AppendLine("## Job Details");
        builder.AppendLine();
        builder.AppendLine("| Job | Report UID | Status | HTTP | Records | Success | Rejected | Retries | Upload | Ingestion | Total |");
        builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var job in result.Jobs.OrderBy(job => job.JobNumber))
        {
            builder.AppendLine(
                $"| {job.JobNumber} | {EscapeMarkdown(job.ReportUid ?? "-")} | {EscapeMarkdown(job.FinalStatus)} | {job.HttpStatusCode} | {job.TotalRecords:N0} | {job.SuccessCount:N0} | {job.RejectCount:N0} | {job.BatchPersistenceRetryCount:N0} | {FormatDuration(job.UploadElapsedMilliseconds)} | {FormatDuration(job.IngestionElapsedMilliseconds)} | {FormatDuration(job.TotalElapsedMilliseconds)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Bottleneck Summary");
        builder.AppendLine();
        builder.AppendLine(BuildBottleneckSummary(result));
        builder.AppendLine();
        builder.AppendLine("## Validation Notes");
        builder.AppendLine();
        builder.AppendLine("- The input workload is generated deterministically from the configured record count and invalid-row percentage.");
        builder.AppendLine("- Raw upload and ingestion responses are saved beside this report for traceability.");
        builder.AppendLine("- A run passes only when every job completes without an infrastructure failure and all input records are accounted for.");
        builder.AppendLine("- Internal stage timings should be correlated with application logs containing `INGESTION_STAGE_COMPLETE` events.");

        var failures = result.Jobs
            .Where(job => !string.IsNullOrWhiteSpace(job.Error))
            .ToList();

        if (failures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failures");
            builder.AppendLine();

            foreach (var failure in failures)
            {
                builder.AppendLine(
                    $"- Job {failure.JobNumber}: {EscapeMarkdown(failure.Error ?? "Unknown error")}");
            }
        }

        return builder.ToString();
    }

    private static string BuildHtml(PerformanceTestResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>GFR Performance Test Results</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: Arial, sans-serif; margin: 32px; color: #1f2937; }");
        builder.AppendLine("    h1, h2 { color: #0f3d62; }");
        builder.AppendLine("    .status { font-size: 1.2rem; font-weight: 700; padding: 10px 14px; border: 1px solid #cbd5e1; display: inline-block; border-radius: 6px; }");
        builder.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 16px 0 28px; }");
        builder.AppendLine("    th, td { border: 1px solid #d1d5db; padding: 8px; text-align: left; }");
        builder.AppendLine("    th { background: #f3f4f6; }");
        builder.AppendLine("    code { background: #f3f4f6; padding: 2px 4px; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <h1>GFR Remediation API — Performance Test Results</h1>");
        builder.AppendLine($"  <p><strong>Run ID:</strong> <code>{Encode(result.TestRunId)}</code><br />");
        builder.AppendLine($"  <strong>Scenario:</strong> <code>{Encode(result.ScenarioName)}</code><br />");
        builder.AppendLine($"  <strong>Environment:</strong> <code>{Encode(result.EnvironmentName)}</code><br />");
        builder.AppendLine($"  <strong>Base URL:</strong> <code>{Encode(result.BaseUrl)}</code><br />");
        builder.AppendLine($"  <strong>Git commit:</strong> <code>{Encode(result.GitCommit)}</code><br />");
        builder.AppendLine($"  <strong>Started:</strong> <code>{result.StartedAtUtc:O}</code><br />");
        builder.AppendLine($"  <strong>Completed:</strong> <code>{result.CompletedAtUtc:O}</code></p>");
        builder.AppendLine();
        builder.AppendLine($"  <div class=\"status\">{(result.Passed ? "PASS ✅" : "FAIL ❌")}</div>");
        builder.AppendLine();
        builder.AppendLine("  <h2>Summary</h2>");
        builder.AppendLine("  <table>");
        builder.AppendLine("    <tr><th>Metric</th><th>Value</th></tr>");
        AppendMetricRow(builder, "Jobs", result.Jobs.Count.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Requested concurrency", result.RequestedConcurrency.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Records per job", result.RecordsPerJob.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Total input records", result.TotalInputRecords.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Total processed records", result.TotalProcessedRecords.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Successful records", result.TotalSucceededRecords.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Rejected records", result.TotalRejectedRecords.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Failed jobs", result.FailedJobCount.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Persistence retries", result.TotalRetryCount.ToString("N0", CultureInfo.InvariantCulture));
        AppendMetricRow(builder, "Input data", FormatBytes(result.TotalInputBytes));
        AppendMetricRow(builder, "Wall time", FormatDuration(result.WallTimeMilliseconds));
        AppendMetricRow(builder, "Throughput", $"{result.RecordsPerSecond:N2} records/sec");
        AppendMetricRow(builder, "Input throughput", $"{result.MegabytesPerSecond:N2} MB/sec");
        AppendMetricRow(builder, "Rejection rate", $"{result.ErrorRatePercentage:N2}%");
        builder.AppendLine("  </table>");
        builder.AppendLine();
        builder.AppendLine("  <h2>Job Details</h2>");
        builder.AppendLine("  <table>");
        builder.AppendLine("    <tr><th>Job</th><th>Report UID</th><th>Status</th><th>HTTP</th><th>Records</th><th>Success</th><th>Rejected</th><th>Retries</th><th>Upload</th><th>Ingestion</th><th>Total</th></tr>");

        foreach (var job in result.Jobs.OrderBy(job => job.JobNumber))
        {
            builder.AppendLine("    <tr>");
            builder.AppendLine($"      <td>{job.JobNumber}</td>");
            builder.AppendLine($"      <td>{Encode(job.ReportUid ?? "-")}</td>");
            builder.AppendLine($"      <td>{Encode(job.FinalStatus)}</td>");
            builder.AppendLine($"      <td>{job.HttpStatusCode}</td>");
            builder.AppendLine($"      <td>{job.TotalRecords:N0}</td>");
            builder.AppendLine($"      <td>{job.SuccessCount:N0}</td>");
            builder.AppendLine($"      <td>{job.RejectCount:N0}</td>");
            builder.AppendLine($"      <td>{job.BatchPersistenceRetryCount:N0}</td>");
            builder.AppendLine($"      <td>{Encode(FormatDuration(job.UploadElapsedMilliseconds))}</td>");
            builder.AppendLine($"      <td>{Encode(FormatDuration(job.IngestionElapsedMilliseconds))}</td>");
            builder.AppendLine($"      <td>{Encode(FormatDuration(job.TotalElapsedMilliseconds))}</td>");
            builder.AppendLine("    </tr>");
        }

        builder.AppendLine("  </table>");
        builder.AppendLine();
        builder.AppendLine("  <h2>Bottleneck Summary</h2>");
        builder.AppendLine($"  <p>{Encode(BuildBottleneckSummary(result))}</p>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static void AppendMetricRow(
        StringBuilder builder,
        string metric,
        string value)
    {
        builder.AppendLine(
            $"    <tr><td>{Encode(metric)}</td><td>{Encode(value)}</td></tr>");
    }

    private static string BuildBottleneckSummary(PerformanceTestResult result)
    {
        if (result.Jobs.Count == 0)
            return "No jobs were executed.";

        var averageUpload = result.Jobs.Average(job => job.UploadElapsedMilliseconds);
        var averageIngestion = result.Jobs.Average(job => job.IngestionElapsedMilliseconds);

        return averageIngestion >= averageUpload
            ? $"The ingestion phase is the dominant API phase. Average ingestion time was {FormatDuration((long)averageIngestion)} compared with {FormatDuration((long)averageUpload)} for upload. Use the application stage-duration logs to isolate parsing, Parquet, DynamoDB, or summary-writing cost."
            : $"The upload phase is the dominant API phase. Average upload time was {FormatDuration((long)averageUpload)} compared with {FormatDuration((long)averageIngestion)} for ingestion. Review request-body transfer, gateway limits, and source-file storage latency.";
    }

    private static string FormatDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture)}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / 1024d / 1024d / 1024d:N2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / 1024d / 1024d:N2} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024d:N2} KB";
        return $"{bytes:N0} bytes";
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Encode(string value) =>
        WebUtility.HtmlEncode(value) ?? string.Empty;
}

namespace RemediationTool.PerformanceTests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(argument =>
                argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(PerformanceTestOptions.Usage);
            return 0;
        }

        if (!PerformanceTestOptions.TryParse(args, out var options, out var error)
            || options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(PerformanceTestOptions.Usage);
            return 1;
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var inputDirectory = Path.Combine(options.OutputDirectory, "input");
        Directory.CreateDirectory(inputDirectory);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var testRunId = $"PERF-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..34];
        var startedAtUtc = DateTime.UtcNow;

        Console.WriteLine($"Starting performance run {testRunId}");
        Console.WriteLine($"Base URL: {options.BaseUri}");
        Console.WriteLine($"Records per job: {options.RecordsPerJob:N0}");
        Console.WriteLine($"Concurrent jobs: {options.Concurrency:N0}");

        try
        {
            using var apiClient = new RemediationApiClient(options);
            var jobs = Enumerable.Range(1, options.Concurrency)
                .Select(jobNumber => ExecuteJobAsync(
                    apiClient,
                    options,
                    inputDirectory,
                    jobNumber,
                    cancellation.Token))
                .ToArray();

            var jobResults = await Task.WhenAll(jobs);
            var completedAtUtc = DateTime.UtcNow;
            var result = new PerformanceTestResult
            {
                TestRunId = testRunId,
                ScenarioName = options.ScenarioName,
                BaseUrl = options.BaseUri.ToString(),
                EnvironmentName = Environment.GetEnvironmentVariable("REMEDIATION_ENVIRONMENT")
                    ?? options.BaseUri.Host,
                GitCommit = Environment.GetEnvironmentVariable("GITHUB_SHA")
                    ?? Environment.GetEnvironmentVariable("BUILD_SOURCEVERSION")
                    ?? "local",
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                RecordsPerJob = options.RecordsPerJob,
                RequestedConcurrency = options.Concurrency,
                InvalidRowPercentage = options.InvalidRowPercentage,
                Jobs = jobResults.OrderBy(job => job.JobNumber).ToArray()
            };

            await PerformanceReportWriter.WriteAsync(
                result,
                options.OutputDirectory,
                cancellation.Token);

            Console.WriteLine();
            Console.WriteLine($"Result: {(result.Passed ? "PASS" : "FAIL")}");
            Console.WriteLine($"Processed: {result.TotalProcessedRecords:N0}");
            Console.WriteLine($"Throughput: {result.RecordsPerSecond:N2} records/sec");
            Console.WriteLine($"Report: {Path.GetFullPath(options.OutputDirectory)}");

            return result.Passed ? 0 : 2;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Performance run cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Performance run failed: {exception}");
            return 1;
        }
        finally
        {
            if (!ReadKeepInputSetting() && Directory.Exists(inputDirectory))
                Directory.Delete(inputDirectory, recursive: true);
        }
    }

    private static async Task<JobExecutionResult> ExecuteJobAsync(
        RemediationApiClient apiClient,
        PerformanceTestOptions options,
        string inputDirectory,
        int jobNumber,
        CancellationToken cancellationToken)
    {
        var inputFilePath = Path.Combine(
            inputDirectory,
            $"performance-input-{jobNumber:D2}.csv");

        Console.WriteLine($"Job {jobNumber:D2}: generating input...");
        var inputFileBytes = await CsvWorkloadGenerator.WriteAsync(
            inputFilePath,
            options.RecordsPerJob,
            options.InvalidRowPercentage,
            jobNumber,
            cancellationToken);

        Console.WriteLine(
            $"Job {jobNumber:D2}: uploading {inputFileBytes / 1024d / 1024d:N2} MB...");
        var result = await apiClient.ExecuteJobAsync(
            jobNumber,
            inputFilePath,
            inputFileBytes,
            cancellationToken);

        Console.WriteLine(
            $"Job {jobNumber:D2}: {result.FinalStatus}; records={result.TotalRecords:N0}; success={result.SuccessCount:N0}; rejected={result.RejectCount:N0}; elapsed={result.TotalElapsedMilliseconds / 1000d:N2}s");
        return result;
    }

    private static bool ReadKeepInputSetting()
    {
        var value = Environment.GetEnvironmentVariable("REMEDIATION_KEEP_INPUT");
        return bool.TryParse(value, out var keepInput) && keepInput;
    }
}

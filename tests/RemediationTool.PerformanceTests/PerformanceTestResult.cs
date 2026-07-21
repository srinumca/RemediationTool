namespace RemediationTool.PerformanceTests;

internal sealed record JobExecutionResult
{
    public required int JobNumber { get; init; }

    public required string InputFileName { get; init; }

    public required long InputFileBytes { get; init; }

    public string? ReportUid { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public required DateTime CompletedAtUtc { get; init; }

    public required long UploadElapsedMilliseconds { get; init; }

    public required long IngestionElapsedMilliseconds { get; init; }

    public required int HttpStatusCode { get; init; }

    public required string FinalStatus { get; init; }

    public required int TotalRecords { get; init; }

    public required int SuccessCount { get; init; }

    public required int RejectCount { get; init; }

    public required int BatchPersistenceRetryCount { get; init; }

    public required int TotalBatches { get; init; }

    public required int PersistedBatchCount { get; init; }

    public required bool IsResumeEligible { get; init; }

    public string? WorkingFileFormat { get; init; }

    public int WorkingFileRecordCount { get; init; }

    public string? Error { get; init; }

    public long TotalElapsedMilliseconds =>
        UploadElapsedMilliseconds + IngestionElapsedMilliseconds;

    public bool CompletedWithoutInfrastructureFailure =>
        string.IsNullOrWhiteSpace(Error)
        && HttpStatusCode is >= 200 and < 300
        && !FinalStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
        && !FinalStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}

internal sealed record PerformanceTestResult
{
    public required string TestRunId { get; init; }

    public required string ScenarioName { get; init; }

    public required string BaseUrl { get; init; }

    public required string EnvironmentName { get; init; }

    public required string GitCommit { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public required DateTime CompletedAtUtc { get; init; }

    public required int RecordsPerJob { get; init; }

    public required int RequestedConcurrency { get; init; }

    public required int InvalidRowPercentage { get; init; }

    public required IReadOnlyList<JobExecutionResult> Jobs { get; init; }

    public long WallTimeMilliseconds =>
        Math.Max(0, (long)(CompletedAtUtc - StartedAtUtc).TotalMilliseconds);

    public long TotalInputRecords => (long)RecordsPerJob * Jobs.Count;

    public long TotalProcessedRecords => Jobs.Sum(job => (long)job.TotalRecords);

    public long TotalSucceededRecords => Jobs.Sum(job => (long)job.SuccessCount);

    public long TotalRejectedRecords => Jobs.Sum(job => (long)job.RejectCount);

    public int FailedJobCount =>
        Jobs.Count(job => !job.CompletedWithoutInfrastructureFailure);

    public int TotalRetryCount => Jobs.Sum(job => job.BatchPersistenceRetryCount);

    public long TotalInputBytes => Jobs.Sum(job => job.InputFileBytes);

    public double RecordsPerSecond => WallTimeMilliseconds == 0
        ? 0
        : TotalProcessedRecords / (WallTimeMilliseconds / 1000d);

    public double MegabytesPerSecond => WallTimeMilliseconds == 0
        ? 0
        : (TotalInputBytes / 1024d / 1024d) / (WallTimeMilliseconds / 1000d);

    public double ErrorRatePercentage => TotalProcessedRecords == 0
        ? 100
        : (TotalRejectedRecords / (double)TotalProcessedRecords) * 100;

    public bool Passed =>
        FailedJobCount == 0
        && TotalProcessedRecords == TotalInputRecords;
}

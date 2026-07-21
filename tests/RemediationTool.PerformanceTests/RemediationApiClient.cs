using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RemediationTool.PerformanceTests;

internal sealed class RemediationApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDirectory;

    public RemediationApiClient(PerformanceTestOptions options)
    {
        _outputDirectory = options.OutputDirectory;
        _httpClient = new HttpClient
        {
            BaseAddress = options.BaseUri,
            Timeout = options.RequestTimeout
        };

        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.BearerToken);
        }
    }

    public async Task<JobExecutionResult> ExecuteJobAsync(
        int jobNumber,
        string inputFilePath,
        long inputFileBytes,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var uploadElapsedMilliseconds = 0L;
        var ingestionElapsedMilliseconds = 0L;
        string? reportUid = null;
        var finalStatusCode = 0;

        try
        {
            using var multipart = new MultipartFormDataContent();
            await using var inputStream = new FileStream(
                inputFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65_536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var fileContent = new StreamContent(inputStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            multipart.Add(fileContent, "file", Path.GetFileName(inputFilePath));

            var uploadStopwatch = Stopwatch.StartNew();
            using var uploadResponse = await _httpClient.PostAsync(
                "api/upload",
                multipart,
                cancellationToken);
            uploadStopwatch.Stop();
            uploadElapsedMilliseconds = uploadStopwatch.ElapsedMilliseconds;
            finalStatusCode = (int)uploadResponse.StatusCode;

            var uploadJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            await WriteResponseAsync(
                $"upload-response-{jobNumber:D2}.json",
                uploadJson,
                cancellationToken);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                return CreateFailure(
                    jobNumber,
                    inputFilePath,
                    inputFileBytes,
                    startedAtUtc,
                    uploadElapsedMilliseconds,
                    ingestionElapsedMilliseconds,
                    finalStatusCode,
                    null,
                    $"Upload failed: {uploadJson}");
            }

            reportUid = ReadString(uploadJson, "reportUid", "jobId");
            if (string.IsNullOrWhiteSpace(reportUid))
            {
                return CreateFailure(
                    jobNumber,
                    inputFilePath,
                    inputFileBytes,
                    startedAtUtc,
                    uploadElapsedMilliseconds,
                    ingestionElapsedMilliseconds,
                    finalStatusCode,
                    null,
                    "Upload succeeded but no ReportUID was returned.");
            }

            var ingestionStopwatch = Stopwatch.StartNew();
            using var ingestionResponse = await _httpClient.PostAsync(
                $"api/ingestion/{Uri.EscapeDataString(reportUid)}",
                content: null,
                cancellationToken: cancellationToken);
            ingestionStopwatch.Stop();
            ingestionElapsedMilliseconds = ingestionStopwatch.ElapsedMilliseconds;
            finalStatusCode = (int)ingestionResponse.StatusCode;

            var ingestionJson = await ingestionResponse.Content.ReadAsStringAsync(cancellationToken);
            await WriteResponseAsync(
                $"ingestion-response-{jobNumber:D2}.json",
                ingestionJson,
                cancellationToken);

            var responseValues = ReadIngestionValues(ingestionJson);

            return new JobExecutionResult
            {
                JobNumber = jobNumber,
                InputFileName = Path.GetFileName(inputFilePath),
                InputFileBytes = inputFileBytes,
                ReportUid = reportUid,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTime.UtcNow,
                UploadElapsedMilliseconds = uploadElapsedMilliseconds,
                IngestionElapsedMilliseconds = ingestionElapsedMilliseconds,
                HttpStatusCode = finalStatusCode,
                FinalStatus = responseValues.Status,
                TotalRecords = responseValues.TotalRecords,
                SuccessCount = responseValues.SuccessCount,
                RejectCount = responseValues.RejectCount,
                BatchPersistenceRetryCount = responseValues.BatchPersistenceRetryCount,
                TotalBatches = responseValues.TotalBatches,
                PersistedBatchCount = responseValues.PersistedBatchCount,
                IsResumeEligible = responseValues.IsResumeEligible,
                WorkingFileFormat = responseValues.WorkingFileFormat,
                WorkingFileRecordCount = responseValues.WorkingFileRecordCount,
                Error = ingestionResponse.IsSuccessStatusCode
                    ? null
                    : $"Ingestion returned HTTP {finalStatusCode}."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateFailure(
                jobNumber,
                inputFilePath,
                inputFileBytes,
                startedAtUtc,
                uploadElapsedMilliseconds,
                ingestionElapsedMilliseconds,
                finalStatusCode,
                reportUid,
                exception.Message);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task WriteResponseAsync(
        string fileName,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_outputDirectory, fileName),
            content,
            cancellationToken);
    }

    private static JobExecutionResult CreateFailure(
        int jobNumber,
        string inputFilePath,
        long inputFileBytes,
        DateTime startedAtUtc,
        long uploadElapsedMilliseconds,
        long ingestionElapsedMilliseconds,
        int statusCode,
        string? reportUid,
        string error)
    {
        return new JobExecutionResult
        {
            JobNumber = jobNumber,
            InputFileName = Path.GetFileName(inputFilePath),
            InputFileBytes = inputFileBytes,
            ReportUid = reportUid,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            UploadElapsedMilliseconds = uploadElapsedMilliseconds,
            IngestionElapsedMilliseconds = ingestionElapsedMilliseconds,
            HttpStatusCode = statusCode,
            FinalStatus = "Failed",
            TotalRecords = 0,
            SuccessCount = 0,
            RejectCount = 0,
            BatchPersistenceRetryCount = 0,
            TotalBatches = 0,
            PersistedBatchCount = 0,
            IsResumeEligible = false,
            WorkingFileFormat = null,
            WorkingFileRecordCount = 0,
            Error = error
        };
    }

    private static IngestionValues ReadIngestionValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return IngestionValues.Empty;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new IngestionValues(
            ReadStatus(root),
            ReadInt(root, "totalRecords"),
            ReadInt(root, "successCount"),
            ReadInt(root, "rejectCount"),
            ReadInt(root, "batchPersistenceRetryCount"),
            ReadInt(root, "totalBatches"),
            ReadInt(root, "persistedBatchCount"),
            ReadBool(root, "isResumeEligible"),
            ReadString(root, "workingFileFormat"),
            ReadInt(root, "workingFileRecordCount"));
    }

    private static string ReadStatus(JsonElement root)
    {
        if (!TryGetProperty(root, "status", out var value))
            return "Unknown";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "Unknown",
            JsonValueKind.Number when value.TryGetInt32(out var number) => number switch
            {
                1 => "Started",
                2 => "Success",
                3 => "PartialSuccess",
                4 => "Failed",
                5 => "Completed",
                _ => number.ToString(CultureInfo.InvariantCulture)
            },
            _ => value.ToString()
        };
    }

    private static string? ReadString(string json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);
        return ReadString(document.RootElement, propertyNames);
    }

    private static string? ReadString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(root, propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var numberValue))
        {
            return numberValue;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue)
                    ? stringValue
                    : 0;
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
            return false;

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var result)
            && result;
    }

    private static bool TryGetProperty(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record IngestionValues(
        string Status,
        int TotalRecords,
        int SuccessCount,
        int RejectCount,
        int BatchPersistenceRetryCount,
        int TotalBatches,
        int PersistedBatchCount,
        bool IsResumeEligible,
        string? WorkingFileFormat,
        int WorkingFileRecordCount)
    {
        public static IngestionValues Empty { get; } = new(
            "Unknown",
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            null,
            0);
    }
}

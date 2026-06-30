// ─────────────────────────────────────────────────────────────────────────────
// FILE: src/RemediationTool.Infrastructure/S3StorageService.cs  (FULL REPLACEMENT)
//
// Builds on the previous logging additions, now wrapped with LogPerformanceScope
// for timing visibility on every S3 call, and proper correlation flow (no extra
// code needed for correlation — it comes through LogContext automatically).
// ─────────────────────────────────────────────────────────────────────────────

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;

namespace RemediationTool.Infrastructure;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly string _bucketName;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client;
        _configuration = configuration;
        _logger = logger;
        _bucketName = configuration["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS:BucketName configuration is missing.");

        _logger.LogInformation("[S3 INIT] S3StorageService initialised. Bucket={Bucket}", _bucketName);
    }

    public async Task UploadAsync(string key, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

        // LogPerformanceScope automatically times the operation and logs
        // [PERF] on success or [PERF SLOW] if it exceeds 1000ms — no manual
        // Stopwatch code needed at any call site across the whole solution.
        using var perf = new LogPerformanceScope(_logger, "S3 Upload", new { Bucket = _bucketName, Key = normalizedKey });

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = normalizedKey,
            InputStream = stream
        };
        ApplyServerSideEncryption(request);

        try
        {
            await _s3Client.PutObjectAsync(request);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex, "[S3 UPLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

        // Threshold raised to 3000ms here — large ingestion files are expected
        // to take longer than the 1000ms default without that being "slow".
        using var perf = new LogPerformanceScope(
            _logger, "S3 Download", new { Bucket = _bucketName, Key = normalizedKey },
            slowThreshold: TimeSpan.FromMilliseconds(3000));

        try
        {
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = normalizedKey
            });

            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            _logger.LogInformation(
                "[S3 DOWNLOAD COMPLETE] Bucket={Bucket} Key={Key} SizeBytes={SizeBytes}",
                _bucketName, normalizedKey, memoryStream.Length);

            return memoryStream;
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            perf.MarkFailed();
            _logger.LogWarning(
                "[S3 DOWNLOAD NOT FOUND] Bucket={Bucket} Key={Key} — object does not exist.",
                _bucketName, normalizedKey);
            throw;
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex, "[S3 DOWNLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("Source key is required.", nameof(sourceKey));
        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));

        var normSource = NormalizeKey(sourceKey);
        var normDest = NormalizeKey(destinationKey);

        using var perf = new LogPerformanceScope(_logger, "S3 Move", new { Bucket = _bucketName, Source = normSource, Destination = normDest });

        try
        {
            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = normSource,
                DestinationBucket = _bucketName,
                DestinationKey = normDest
            };
            ApplyServerSideEncryption(copyRequest);

            await _s3Client.CopyObjectAsync(copyRequest);
            await DeleteAsync(sourceKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex, "[S3 MOVE FAILED] Bucket={Bucket} Source={Source} Destination={Destination} Error={Error}",
                _bucketName, normSource, normDest, ex.Message);
            throw;
        }
    }

    public async Task DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);
        using var perf = new LogPerformanceScope(_logger, "S3 Delete", new { Bucket = _bucketName, Key = normalizedKey });

        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, normalizedKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex, "[S3 DELETE FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    // ── existing private helpers (NormalizeKey, ApplyServerSideEncryption)
    //    remain unchanged.
}
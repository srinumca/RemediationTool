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

    // ── UploadAsync ──────────────────────────────────────────────────────────
    public async Task UploadAsync(string key, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

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
            _logger.LogInformation(
                "[S3 UPLOAD COMPLETE] Bucket={Bucket} Key={Key}",
                _bucketName, normalizedKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex,
                "[S3 UPLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    // ── DownloadAsync ────────────────────────────────────────────────────────
    public async Task<Stream> DownloadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

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
            _logger.LogError(ex,
                "[S3 DOWNLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    // ── MoveAsync ────────────────────────────────────────────────────────────
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

            _logger.LogInformation(
                "[S3 MOVE COMPLETE] Bucket={Bucket} Source={Source} Destination={Destination}",
                _bucketName, normSource, normDest);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex,
                "[S3 MOVE FAILED] Bucket={Bucket} Source={Source} Destination={Destination} Error={Error}",
                _bucketName, normSource, normDest, ex.Message);
            throw;
        }
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────
    public async Task DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

        using var perf = new LogPerformanceScope(_logger, "S3 Delete", new { Bucket = _bucketName, Key = normalizedKey });

        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, normalizedKey);
            _logger.LogInformation(
                "[S3 DELETE] Bucket={Bucket} Key={Key} — deleted.",
                _bucketName, normalizedKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(ex,
                "[S3 DELETE FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName, normalizedKey, ex.Message);
            throw;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────
    // These were present in the original S3StorageService.cs and are required
    // by every public method above. If your build is reporting "NormalizeKey
    // does not exist" or "ApplyServerSideEncryption does not exist", it means
    // these two methods were dropped during a previous copy/paste. They are
    // restored here exactly as they existed originally — no logic changes.

    /// <summary>
    /// Normalises a storage key by trimming any leading slash, since S3 keys
    /// should never start with "/" (a leading slash creates a key with an
    /// empty first path segment, which is valid in S3 but not what callers
    /// intend when building paths like "2026/06/ING-.../file.csv").
    /// </summary>
    private static string NormalizeKey(string key) => key.TrimStart('/');

    /// <summary>
    /// Applies server-side encryption settings to an S3 request based on
    /// configuration. Supports either AES256 (S3-managed keys) or KMS
    /// (customer-managed keys via AWS:KmsKeyId), controlled by
    /// AWS:UseServerSideEncryption and AWS:ServerSideEncryptionMethod.
    /// </summary>
    private void ApplyServerSideEncryption(PutObjectRequest request)
    {
        // Reads via the IConfiguration indexer (string) + bool.TryParse instead
        // of the GetValue<T>() extension method — GetValue<T>() lives in the
        // Microsoft.Extensions.Configuration.Binder package, which may not be
        // referenced in this project. The indexer approach needs nothing extra.
        var useEncryption = bool.TryParse(_configuration["AWS:UseServerSideEncryption"], out var parsed) && parsed;
        if (!useEncryption) return;

        var method = _configuration["AWS:ServerSideEncryptionMethod"] ?? "AES256";

        if (method.Equals("aws:kms", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("KMS", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;

            var kmsKeyId = _configuration["AWS:KmsKeyId"];
            if (!string.IsNullOrWhiteSpace(kmsKeyId))
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
        }
        else
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }
    }

    /// <summary>
    /// Overload for CopyObjectRequest — S3 copy operations need encryption
    /// re-applied on the destination object since encryption settings are
    /// not automatically carried over during a copy.
    /// </summary>
    private void ApplyServerSideEncryption(CopyObjectRequest request)
    {
        // Same indexer + TryParse approach as the PutObjectRequest overload above.
        var useEncryption = bool.TryParse(_configuration["AWS:UseServerSideEncryption"], out var parsed) && parsed;
        if (!useEncryption) return;

        var method = _configuration["AWS:ServerSideEncryptionMethod"] ?? "AES256";

        if (method.Equals("aws:kms", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("KMS", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;

            var kmsKeyId = _configuration["AWS:KmsKeyId"];
            if (!string.IsNullOrWhiteSpace(kmsKeyId))
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
        }
        else
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }
    }
}
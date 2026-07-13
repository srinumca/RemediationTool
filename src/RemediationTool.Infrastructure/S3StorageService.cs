using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using System.Net;

namespace RemediationTool.Infrastructure;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<S3StorageService> _logger;
    private readonly bool _useServerSideEncryption;
    private readonly ServerSideEncryptionMethod _encryptionMethod;
    private readonly string? _kmsKeyId;

    public S3StorageService(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
        _bucketName = configuration["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS:BucketName configuration is missing.");

        _useServerSideEncryption =
            bool.TryParse(configuration["AWS:UseServerSideEncryption"], out var parsedEncryptionFlag)
            && parsedEncryptionFlag;

        var configuredMethod = configuration["AWS:ServerSideEncryptionMethod"] ?? "AES256";
        _encryptionMethod = configuredMethod.Equals("aws:kms", StringComparison.OrdinalIgnoreCase)
                            || configuredMethod.Equals("KMS", StringComparison.OrdinalIgnoreCase)
            ? ServerSideEncryptionMethod.AWSKMS
            : ServerSideEncryptionMethod.AES256;

        _kmsKeyId = configuration["AWS:KmsKeyId"];

        _logger.LogInformation(
            "[S3 INIT] S3StorageService initialised. Bucket={Bucket} EncryptionEnabled={EncryptionEnabled} EncryptionMethod={EncryptionMethod}",
            _bucketName,
            _useServerSideEncryption,
            _encryptionMethod);
    }

    public async Task UploadAsync(string key, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        ArgumentNullException.ThrowIfNull(stream);

        var normalizedKey = NormalizeKey(key);

        using var perf = new LogPerformanceScope(
            _logger,
            "S3 Upload",
            new { Bucket = _bucketName, Key = normalizedKey });

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
                _bucketName,
                normalizedKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(
                ex,
                "[S3 UPLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName,
                normalizedKey,
                ex.Message);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

        using var perf = new LogPerformanceScope(
            _logger,
            "S3 Download",
            new { Bucket = _bucketName, Key = normalizedKey },
            slowThreshold: TimeSpan.FromMilliseconds(3000));

        try
        {
            using var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = normalizedKey
            });

            var initialCapacity = response.ContentLength is > 0 and <= int.MaxValue
                ? (int)response.ContentLength
                : 0;

            var memoryStream = initialCapacity > 0
                ? new MemoryStream(initialCapacity)
                : new MemoryStream();

            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            _logger.LogInformation(
                "[S3 DOWNLOAD COMPLETE] Bucket={Bucket} Key={Key} SizeBytes={SizeBytes}",
                _bucketName,
                normalizedKey,
                memoryStream.Length);

            return memoryStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            perf.MarkFailed();
            _logger.LogWarning(
                "[S3 DOWNLOAD NOT FOUND] Bucket={Bucket} Key={Key} — object does not exist.",
                _bucketName,
                normalizedKey);
            throw;
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(
                ex,
                "[S3 DOWNLOAD FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName,
                normalizedKey,
                ex.Message);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var normalizedKey = NormalizeKey(key);

        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = normalizedKey
            });

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("Source key is required.", nameof(sourceKey));

        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));

        var normalizedSource = NormalizeKey(sourceKey);
        var normalizedDestination = NormalizeKey(destinationKey);

        using var perf = new LogPerformanceScope(
            _logger,
            "S3 Move",
            new
            {
                Bucket = _bucketName,
                Source = normalizedSource,
                Destination = normalizedDestination
            });

        try
        {
            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = normalizedSource,
                DestinationBucket = _bucketName,
                DestinationKey = normalizedDestination
            };

            ApplyServerSideEncryption(copyRequest);

            await _s3Client.CopyObjectAsync(copyRequest);
            await DeleteAsync(sourceKey);

            _logger.LogInformation(
                "[S3 MOVE COMPLETE] Bucket={Bucket} Source={Source} Destination={Destination}",
                _bucketName,
                normalizedSource,
                normalizedDestination);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(
                ex,
                "[S3 MOVE FAILED] Bucket={Bucket} Source={Source} Destination={Destination} Error={Error}",
                _bucketName,
                normalizedSource,
                normalizedDestination,
                ex.Message);
            throw;
        }
    }

    public async Task DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var normalizedKey = NormalizeKey(key);

        using var perf = new LogPerformanceScope(
            _logger,
            "S3 Delete",
            new { Bucket = _bucketName, Key = normalizedKey });

        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, normalizedKey);
            _logger.LogInformation(
                "[S3 DELETE] Bucket={Bucket} Key={Key} — deleted.",
                _bucketName,
                normalizedKey);
        }
        catch (Exception ex)
        {
            perf.MarkFailed();
            _logger.LogError(
                ex,
                "[S3 DELETE FAILED] Bucket={Bucket} Key={Key} Error={Error}",
                _bucketName,
                normalizedKey,
                ex.Message);
            throw;
        }
    }

    private static string NormalizeKey(string key) => key.TrimStart('/');

    private void ApplyServerSideEncryption(PutObjectRequest request)
    {
        if (!_useServerSideEncryption)
            return;

        request.ServerSideEncryptionMethod = _encryptionMethod;

        if (_encryptionMethod == ServerSideEncryptionMethod.AWSKMS
            && !string.IsNullOrWhiteSpace(_kmsKeyId))
        {
            request.ServerSideEncryptionKeyManagementServiceKeyId = _kmsKeyId;
        }
    }

    private void ApplyServerSideEncryption(CopyObjectRequest request)
    {
        if (!_useServerSideEncryption)
            return;

        request.ServerSideEncryptionMethod = _encryptionMethod;

        if (_encryptionMethod == ServerSideEncryptionMethod.AWSKMS
            && !string.IsNullOrWhiteSpace(_kmsKeyId))
        {
            request.ServerSideEncryptionKeyManagementServiceKeyId = _kmsKeyId;
        }
    }
}

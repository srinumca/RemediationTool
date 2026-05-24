using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly string _bucketName;

    public S3StorageService(
        IAmazonS3 s3Client,
        IConfiguration configuration)
    {
        _s3Client = s3Client;
        _configuration = configuration;
        _bucketName = configuration["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS:BucketName configuration is missing.");
    }

    public async Task UploadAsync(string key, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = NormalizeKey(key),
            InputStream = stream
        };

        ApplyServerSideEncryption(request);

        await _s3Client.PutObjectAsync(request);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = NormalizeKey(key)
        });

        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        return memoryStream;
    }

    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("Source key is required.", nameof(sourceKey));

        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = _bucketName,
            SourceKey = NormalizeKey(sourceKey),
            DestinationBucket = _bucketName,
            DestinationKey = NormalizeKey(destinationKey)
        };

        ApplyServerSideEncryption(copyRequest);

        await _s3Client.CopyObjectAsync(copyRequest);

        await DeleteAsync(sourceKey);
    }

    public async Task DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = NormalizeKey(key)
        });
    }

    private void ApplyServerSideEncryption(PutObjectRequest request)
    {
        var useEncryption = bool.TryParse(
            _configuration["AWS:UseServerSideEncryption"],
            out var parsedValue) && parsedValue;

        if (!useEncryption)
            return;

        var method = _configuration["AWS:ServerSideEncryptionMethod"] ?? "AES256";

        if (method.Equals("AWSKMS", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;

            var kmsKeyId = _configuration["AWS:KmsKeyId"];

            if (!string.IsNullOrWhiteSpace(kmsKeyId))
            {
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
            }

            return;
        }

        request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
    }

    private void ApplyServerSideEncryption(CopyObjectRequest request)
    {
        var useEncryption = bool.TryParse(
            _configuration["AWS:UseServerSideEncryption"],
            out var parsedValue) && parsedValue;

        if (!useEncryption)
            return;

        var method = _configuration["AWS:ServerSideEncryptionMethod"] ?? "AES256";

        if (method.Equals("AWSKMS", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;

            var kmsKeyId = _configuration["AWS:KmsKeyId"];

            if (!string.IsNullOrWhiteSpace(kmsKeyId))
            {
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
            }

            return;
        }

        request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
    }

    private static string NormalizeKey(string key)
    {
        return key.Replace("\\", "/");
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly string _bucket;
        private readonly ILogger<S3StorageService> _logger;

        public S3StorageService(IAmazonS3 s3, IConfiguration config, ILogger<S3StorageService> logger)
        {
            _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
            _bucket = config?["AWS:BucketName"] ?? throw new ArgumentException("AWS:BucketName is not configured.", nameof(config));
            _logger = logger;
        }

        public async Task UploadAsync(string key, Stream data)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
                if (data == null) throw new ArgumentNullException(nameof(data));

                var request = new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    InputStream = data
                };

                await _s3.PutObjectAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 upload failed for key {Key}", key);
                throw;
            }
        }

        public async Task<Stream> DownloadAsync(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

                using var res = await _s3.GetObjectAsync(_bucket, key).ConfigureAwait(false);
                var ms = new MemoryStream();
                await res.ResponseStream.CopyToAsync(ms).ConfigureAwait(false);
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 download failed for key {Key}", key);
                throw;
            }
        }

        public async Task MoveAsync(string sourceKey, string destinationKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentException("Source key is required.", nameof(sourceKey));
                if (string.IsNullOrWhiteSpace(destinationKey)) throw new ArgumentException("Destination key is required.", nameof(destinationKey));

                var copyRequest = new CopyObjectRequest
                {
                    SourceBucket = _bucket,
                    SourceKey = sourceKey,
                    DestinationBucket = _bucket,
                    DestinationKey = destinationKey
                };

                await _s3.CopyObjectAsync(copyRequest).ConfigureAwait(false);
                await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = sourceKey }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 move failed from {Source} to {Dest}", sourceKey, destinationKey);
                throw;
            }
        }

        public async Task DeleteAsync(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

                await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 delete failed for key {Key}", key);
                throw;
            }
        }
    }
}
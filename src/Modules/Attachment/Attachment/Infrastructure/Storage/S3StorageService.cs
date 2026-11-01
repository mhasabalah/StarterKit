using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Production-grade AWS S3 storage service implementation.
/// Supports server-side copy, batch operations, CDN integration, and encryption.
/// </summary>
public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(
        IAmazonS3 s3Client,
        IOptions<AttachmentOptions> options,
        ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client;
        _options = options.Value.Storage.S3;
        _logger = logger;
    }

    public async Task<StorageResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create S3 path: yyyy/MM/dd/guid_filename
            var now = DateTime.UtcNow;
            var objectKey =
                $"{now:yyyy}/{now:MM}/{now:dd}/{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

            // Upload using TransferUtility for optimized multipart uploads
            var transferUtility = new TransferUtility(_s3Client);

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                InputStream = fileStream,
                ContentType = contentType,
                StorageClass = ParseStorageClass(_options.StorageClass)
            };

            // Add server-side encryption if enabled
            if (_options.UseServerSideEncryption)
            {
                uploadRequest.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            // Add metadata
            uploadRequest.Metadata.Add("original-filename", fileName);
            uploadRequest.Metadata.Add("uploaded-at", DateTime.UtcNow.ToString("O"));

            await transferUtility.UploadAsync(uploadRequest, cancellationToken);

            // Get object metadata for file size
            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            };

            var metadata =
                await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);

            // Generate public URL (or presigned URL for private files)
            var publicUrl = GeneratePublicUrl(objectKey);

            _logger.LogInformation(
                "File uploaded to S3: Bucket={Bucket}, Key={Key}, Size={Size} bytes",
                _options.BucketName,
                objectKey,
                metadata.ContentLength);

            return new StorageResult(objectKey, publicUrl, metadata.ContentLength);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error uploading file: {FileName}, ErrorCode={ErrorCode}",
                fileName, ex.ErrorCode);
            throw new InvalidOperationException($"Failed to upload file to S3: {fileName}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to S3: {FileName}", fileName);
            throw new InvalidOperationException($"Failed to upload file to S3: {fileName}", ex);
        }
    }

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var transferUtility = new TransferUtility(_s3Client);

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath,
                InputStream = fileStream,
                ContentType = contentType,
                StorageClass = ParseStorageClass(_options.StorageClass)
            };

            if (_options.UseServerSideEncryption)
            {
                uploadRequest.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            await transferUtility.UploadAsync(uploadRequest, cancellationToken);

            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath
            };

            var metadata =
                await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
            var publicUrl = GeneratePublicUrl(storagePath);

            _logger.LogInformation(
                "File uploaded to specific path in S3: Key={Key}, Size={Size} bytes",
                storagePath,
                metadata.ContentLength);

            return new StorageResult(storagePath, publicUrl, metadata.ContentLength);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 error uploading file to path: {Path}, ErrorCode={ErrorCode}",
                storagePath, ex.ErrorCode);
            throw new InvalidOperationException($"Failed to upload file to S3 path: {storagePath}",
                ex);
        }
    }

    public async Task<Stream> DownloadAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath
            };

            var response = await _s3Client.GetObjectAsync(request, cancellationToken);

            // Copy to MemoryStream to avoid stream disposal issues
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            _logger.LogInformation("File downloaded from S3: {Key}", storagePath);

            return memoryStream;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new FileNotFoundException($"S3 object not found: {storagePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from S3: {Path}", storagePath);
            throw;
        }
    }

    public async Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);

            _logger.LogInformation("File deleted from S3: {Key}", storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from S3: {Path}", storagePath);
            throw;
        }
    }

    public async Task DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathsList = storagePaths.ToList();

            if (!pathsList.Any())
            {
                return;
            }

            // S3 DeleteObjects supports up to 1000 keys per request
            const int batchSize = 1000;
            var batches = pathsList.Chunk(batchSize);

            foreach (var batch in batches)
            {
                var request = new DeleteObjectsRequest
                {
                    BucketName = _options.BucketName,
                    Objects = batch.Select(key => new KeyVersion { Key = key }).ToList()
                };

                var response = await _s3Client.DeleteObjectsAsync(request, cancellationToken);

                if (response.DeleteErrors is not null
                    && response.DeleteErrors.Count > 0)
                {
                    foreach (var error in response.DeleteErrors)
                    {
                        _logger.LogWarning(
                            "Failed to delete S3 object: Key={Key}, Code={Code}, Message={Message}",
                            error.Key,
                            error.Code,
                            error.Message);
                    }
                }
            }

            _logger.LogInformation("Batch deleted {Count} files from S3", pathsList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch deleting files from S3");
            throw;
        }
    }

    public async Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Server-side copy - no data transfer, very fast!
            var request = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = sourceStoragePath,
                DestinationBucket = _options.BucketName,
                DestinationKey = destinationStoragePath,
                StorageClass = ParseStorageClass(_options.StorageClass)
            };

            if (_options.UseServerSideEncryption)
            {
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            var response = await _s3Client.CopyObjectAsync(request, cancellationToken);

            // Get metadata for file size
            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = destinationStoragePath
            };

            var metadata =
                await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
            var publicUrl = GeneratePublicUrl(destinationStoragePath);

            _logger.LogInformation(
                "File copied in S3: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return new StorageResult(destinationStoragePath, publicUrl, metadata.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new FileNotFoundException($"Source S3 object not found: {sourceStoragePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error copying file in S3: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);
            throw;
        }
    }

    public async Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Copy then delete source
            var result = await CopyAsync(sourceStoragePath, destinationStoragePath,
                cancellationToken);
            await DeleteAsync(sourceStoragePath, cancellationToken);

            _logger.LogInformation(
                "File moved in S3: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error moving file in S3: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);
            throw;
        }
    }

    public Task<string> GetPublicUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var publicUrl = GeneratePublicUrl(storagePath);
        return Task.FromResult(publicUrl);
    }

    public async Task<bool> ExistsAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath
            };

            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when
            (ex.ErrorCode == "NoSuchKey" || ex.ErrorCode == "NotFound")
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file exists in S3: {Path}", storagePath);
            return false;
        }
    }

    /// <summary>
    /// Generates public URL for S3 object.
    /// Uses CloudFront domain if configured, otherwise S3 direct URL.
    /// For private files, generates presigned URL.
    /// </summary>
    private string GeneratePublicUrl(string objectKey)
    {
        // If CloudFront CDN is configured, use it
        if (!string.IsNullOrEmpty(_options.CloudFrontDomain))
        {
            return $"https://{_options.CloudFrontDomain}/{objectKey}";
        }

        // If using public access, return direct S3 URL
        if (_options.UsePublicAccess)
        {
            return $"https://{_options.BucketName}.s3.{_options.Region}.amazonaws.com/{objectKey}";
        }

        // For private files, generate presigned URL
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddMinutes(_options.PresignedUrlExpirationMinutes)
        };

        return _s3Client.GetPreSignedURL(request);
    }

    /// <summary>
    /// Parses storage class string to S3StorageClass enum
    /// </summary>
    private static S3StorageClass ParseStorageClass(string storageClass)
    {
        return storageClass.ToUpperInvariant() switch
        {
            "STANDARD" => S3StorageClass.Standard,
            "REDUCED_REDUNDANCY" => S3StorageClass.ReducedRedundancy,
            "STANDARD_IA" => S3StorageClass.StandardInfrequentAccess,
            "ONEZONE_IA" => S3StorageClass.OneZoneInfrequentAccess,
            "INTELLIGENT_TIERING" => S3StorageClass.IntelligentTiering,
            "GLACIER" => S3StorageClass.Glacier,
            "DEEP_ARCHIVE" => S3StorageClass.DeepArchive,
            "GLACIER_IR" => S3StorageClass.GlacierInstantRetrieval,
            _ => S3StorageClass.Standard
        };
    }
}

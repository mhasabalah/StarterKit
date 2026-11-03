using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// AWS S3 storage. Objects are private; reads use short-lived presigned GET URLs with
/// Content-Disposition / Content-Type overrides. Credentials come from configuration or, when empty,
/// the default AWS credential chain (IAM role).
/// </summary>
public sealed class S3StorageService(
    IAmazonS3 s3Client,
    IOptions<AttachmentOptions> options,
    ILogger<S3StorageService> logger) : IStorageService, IStorageListing
{
    private readonly S3StorageOptions _options = options.Value.Storage.S3;

    public bool ServesThroughApplication => false;

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        var knownLength = StorageStreams.RemainingLength(fileStream);
        try
        {
            var request = new TransferUtilityUploadRequest
            {
                BucketName = _options.BucketName,
                Key = storagePath,
                InputStream = fileStream,
                AutoCloseStream = false,
                ContentType = contentType,
                StorageClass = ParseStorageClass(_options.StorageClass)
            };

            if (_options.UseServerSideEncryption)
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;

            await new TransferUtility(s3Client).UploadAsync(request, cancellationToken);

            // The upload length is known for seekable streams (every upload path): no extra HEAD request.
            if (knownLength is { } length)
                return new StorageResult(storagePath, length);

            var metadata = await s3Client.GetObjectMetadataAsync(_options.BucketName, storagePath, cancellationToken);
            return new StorageResult(storagePath, metadata.ContentLength);
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 error uploading {Key} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to store the file.", ex);
        }
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        try
        {
            var response = await s3Client.GetObjectAsync(_options.BucketName, storagePath, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new StoredFileNotFoundException(storagePath);
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 error reading {Key} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to read the file.", ex);
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        try
        {
            await s3Client.DeleteObjectAsync(_options.BucketName, storagePath, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 error deleting {Key} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to delete the file.", ex);
        }
    }

    public async Task<BatchDeleteResult> DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        var paths = storagePaths.Distinct().ToList();
        if (paths.Count == 0)
            return BatchDeleteResult.Success;

        var failed = new List<string>();
        foreach (var batch in paths.Chunk(1000)) // DeleteObjects accepts up to 1000 keys
        {
            try
            {
                var response = await s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _options.BucketName,
                    Objects = batch.Select(key => new KeyVersion { Key = key }).ToList()
                }, cancellationToken);

                foreach (var error in response.DeleteErrors ?? [])
                {
                    logger.LogWarning("Failed to delete S3 object {Key}: {Code} {Message}",
                        error.Key, error.Code, error.Message);
                    failed.Add(error.Key);
                }
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, "S3 batch delete failed ({ErrorCode})", ex.ErrorCode);
                failed.AddRange(batch);
            }
        }

        return failed.Count == 0 ? BatchDeleteResult.Success : new BatchDeleteResult(failed);
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        try
        {
            await s3Client.GetObjectMetadataAsync(_options.BucketName, storagePath, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public async Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(sourceStoragePath);
        StoragePaths.Validate(destinationStoragePath);
        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = sourceStoragePath,
                DestinationBucket = _options.BucketName,
                DestinationKey = destinationStoragePath,
                StorageClass = ParseStorageClass(_options.StorageClass)
            };

            if (_options.UseServerSideEncryption)
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;

            await s3Client.CopyObjectAsync(request, cancellationToken);

            var metadata = await s3Client.GetObjectMetadataAsync(
                _options.BucketName, destinationStoragePath, cancellationToken);
            return new StorageResult(destinationStoragePath, metadata.ContentLength);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new StoredFileNotFoundException(sourceStoragePath);
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 error copying {Source} to {Destination}", sourceStoragePath, destinationStoragePath);
            throw new FileStorageException("Failed to copy the file.", ex);
        }
    }

    public async Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        var result = await CopyAsync(sourceStoragePath, destinationStoragePath, cancellationToken);
        await DeleteAsync(sourceStoragePath, cancellationToken);
        return result;
    }

    public async Task<string> GetReadUrlAsync(
        string storagePath,
        TimeSpan ttl,
        ReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = storagePath,
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            Expires = DateTime.UtcNow.Add(ttl)
        };

        request.ResponseHeaderOverrides.ContentDisposition =
            FileResponseHeaders.ContentDisposition(options?.FileName, options?.Inline == true);

        if (!string.IsNullOrEmpty(options?.ContentType))
            request.ResponseHeaderOverrides.ContentType = options.ContentType;

        return await s3Client.GetPreSignedURLAsync(request);
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request { BucketName = _options.BucketName, MaxKeys = 1000, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await s3Client.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response.S3Objects ?? [])
            {
                // Unknown time = treat as brand new, so the orphan scan never deletes it.
                DateTime? modified = item.LastModified;
                var lastModified = modified is { } m
                    ? new DateTimeOffset(DateTime.SpecifyKind(m.ToUniversalTime(), DateTimeKind.Utc))
                    : DateTimeOffset.UtcNow;
                yield return new StoredObjectInfo(item.Key, lastModified);
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true && !string.IsNullOrEmpty(response.NextContinuationToken));
    }

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode is "NoSuchKey" or "NotFound";

    private static S3StorageClass ParseStorageClass(string storageClass) =>
        storageClass.ToUpperInvariant() switch
        {
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

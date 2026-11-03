# AWS S3 Storage Implementation - Production Grade

> **Updated:** objects are private and read through presigned GET URLs (`Attachment:Download:ReadUrlTtlMinutes`, default 5 min). Empty `AccessKeyId`/`SecretAccessKey` = default AWS credential chain (IAM role). `UsePublicAccess`, `CloudFrontDomain` and `PresignedUrlExpirationMinutes` are ignored; do not use `--acl public-read`. See [Security.md](Security.md).

## Overview
Implemented a **production-ready AWS S3 storage service** with server-side copy, batch operations, CDN support, encryption, and multiple storage classes.

## ✅ Features Implemented

### 1. **Server-Side Copy** (Fast & Efficient)
```csharp
// No data transfer - S3 copies internally
await storageService.CopyAsync(
    "temp/session-123/photo.jpg",
    "properties/prop-456/photo.jpg");
// ✅ Instant copy, no bandwidth used
```

### 2. **Batch Delete** (Up to 1000 files per request)
```csharp
await storageService.DeleteBatchAsync(new[]
{
    "temp/session-1/file1.jpg",
    "temp/session-1/file2.jpg",
    // ... up to 1000 files
});
// ✅ Single API call
```

### 3. **CloudFront CDN Integration**
```csharp
// Auto-generates CDN URLs if configured
// https://d1234567890abc.cloudfront.net/properties/123/photo.jpg
```

### 4. **Server-Side Encryption** (AES-256)
```csharp
// All files encrypted at rest
UseServerSideEncryption = true
```

### 5. **Storage Classes** (Cost Optimization)
```csharp
StorageClass = "INTELLIGENT_TIERING" // Auto-optimizes costs
// Other options: STANDARD, STANDARD_IA, GLACIER, DEEP_ARCHIVE, etc.
```

### 6. **Public & Private Files**
```csharp
// Public files: Direct S3 URLs
UsePublicAccess = true
// Private files: Presigned URLs (expires after 60 min)
UsePublicAccess = false
```

### 7. **TransferUtility** (Optimized Multipart Uploads)
- Automatically uses multipart upload for files > 5MB
- Concurrent chunk uploads for better performance
- Automatic retry logic

---

## 📦 NuGet Packages Added

```xml
<PackageVersion Include="AWSSDK.S3" Version="4.0.0.3" />
<PackageVersion Include="AWSSDK.Core" Version="4.0.0.3" />
<PackageVersion Include="AWSSDK.Extensions.NETCore.Setup" Version="4.0.0" />
```

---

## ⚙️ Configuration

### appsettings.json

```json
{
  "Attachment": {
    "Storage": {
      "Provider": "S3",
      "S3": {
        "AccessKeyId": "YOUR_AWS_ACCESS_KEY_ID",
        "SecretAccessKey": "YOUR_AWS_SECRET_ACCESS_KEY",
        "Region": "us-east-1",
        "BucketName": "starterkit-attachments",
        "UsePublicAccess": true,
        "CloudFrontDomain": "d1234567890abc.cloudfront.net",
        "UseServerSideEncryption": true,
        "StorageClass": "INTELLIGENT_TIERING",
        "PresignedUrlExpirationMinutes": 60
      }
    }
  }
}
```

### Using Environment Variables (Production)

```bash
# AWS Credentials (NEVER commit these!)
export ATTACHMENT__STORAGE__S3__ACCESSKEYID="YOUR_ACCESS_KEY"
export ATTACHMENT__STORAGE__S3__SECRETACCESSKEY="YOUR_SECRET_KEY"
export ATTACHMENT__STORAGE__S3__REGION="us-east-1"
export ATTACHMENT__STORAGE__S3__BUCKETNAME="starterkit-attachments"
```

### Using Docker Secrets (Recommended)

```bash
# Create secrets
echo "YOUR_AWS_ACCESS_KEY_ID" | docker secret create aws_access_key_id -
echo "YOUR_AWS_SECRET_ACCESS_KEY" | docker secret create aws_secret_access_key -
```

---

## 🔑 Configuration Options Explained

| Option | Description | Default | Example |
|--------|-------------|---------|---------|
| `AccessKeyId` | AWS Access Key ID | null | `YOUR_AWS_ACCESS_KEY_ID` |
| `SecretAccessKey` | AWS Secret Access Key | null | `wJalrXUtnFEMI...` |
| `Region` | AWS Region | `us-east-1` | `eu-west-1`, `ap-southeast-1` |
| `BucketName` | S3 Bucket Name | `attachments` | `starterkit-prod-files` |
| `UsePublicAccess` | Public read access | `true` | `false` for private |
| `CloudFrontDomain` | CDN domain (optional) | `null` | `d123.cloudfront.net` |
| `UseServerSideEncryption` | AES-256 encryption | `true` | Always recommended |
| `StorageClass` | S3 storage class | `STANDARD` | `INTELLIGENT_TIERING` |
| `PresignedUrlExpirationMinutes` | Private URL expiry | `60` | `1440` (24h) |

---

## 🌍 Available AWS Regions

```
us-east-1      (N. Virginia)
us-east-2      (Ohio)
us-west-1      (N. California)
us-west-2      (Oregon)
eu-west-1      (Ireland)
eu-central-1   (Frankfurt)
ap-southeast-1 (Singapore)
ap-northeast-1 (Tokyo)
```

Full list: https://docs.aws.amazon.com/general/latest/gr/s3.html

---

## 💾 Storage Classes (Cost Optimization)

| Class | Use Case | Cost | Retrieval |
|-------|----------|------|-----------|
| `STANDARD` | Frequently accessed | $$$ | Instant |
| `INTELLIGENT_TIERING` | Unknown access pattern | Auto | Instant |
| `STANDARD_IA` | Infrequent access | $$ | Instant |
| `ONEZONE_IA` | Non-critical data | $ | Instant |
| `GLACIER_IR` | Archive, instant retrieval | $ | Instant |
| `GLACIER` | Long-term archive | ¢ | Minutes |
| `DEEP_ARCHIVE` | Rare access | ¢¢ | 12 hours |

**Recommendation:** Use `INTELLIGENT_TIERING` - it auto-optimizes costs based on access patterns.

---

## 📂 File Structure in S3

```
starterkit-attachments/
├── 2026/01/15/
│   ├── {guid}_house1.jpg
│   ├── {guid}_house2.jpg
│   └── thumb_{guid}_house1.jpg
├── temp/
│   └── {sessionId}/
│       ├── {guid}_photo1.jpg
│       └── thumb_{guid}_photo1.jpg
└── properties/
    └── {propertyId}/
        ├── {guid}_main.jpg
        └── thumb_{guid}_main.jpg
```

---

## 🚀 Usage Examples

### Basic Upload
```csharp
var result = await storageService.UploadAsync(
    fileStream,
    "house-photo.jpg",
    "image/jpeg");

Console.WriteLine(result.PublicUrl);
// Output: https://starterkit-attachments.s3.us-east-1.amazonaws.com/2026/01/15/abc123_house-photo.jpg
```

### Server-Side Copy (Fast!)
```csharp
// Copy from temp to permanent storage - NO DOWNLOAD/UPLOAD!
var result = await storageService.CopyAsync(
    sourceStoragePath: "temp/session-123/photo.jpg",
    destinationStoragePath: "properties/456/photo.jpg");

// ✅ Copied in <1 second, no bandwidth used
```

### Batch Delete
```csharp
await storageService.DeleteBatchAsync(new[]
{
    "temp/session-1/file1.jpg",
    "temp/session-1/file2.jpg",
    "temp/session-1/file3.jpg"
});
// ✅ Single API call deletes all
```

### With CloudFront CDN
```csharp
// appsettings.json:
// "CloudFrontDomain": "d1234567890abc.cloudfront.net"

var url = await storageService.GetPublicUrlAsync("properties/123/photo.jpg");
// Output: https://d1234567890abc.cloudfront.net/properties/123/photo.jpg
// ✅ Served from edge locations worldwide
```

---

## 🔒 Security Best Practices

### 1. **NEVER Commit Credentials**
```bash
# ❌ BAD - Do NOT do this
"AccessKeyId": "YOUR_AWS_ACCESS_KEY_ID"

# ✅ GOOD - Use environment variables or secrets
"AccessKeyId": null  # Set via environment
```

### 2. **Use IAM Roles (EC2/ECS/Lambda)**
```csharp
// No credentials needed - uses instance role
services.AddDefaultAWSOptions(Configuration.GetAWSOptions());
services.AddAWSService<IAmazonS3>();
```

### 3. **Enable Bucket Encryption**
```bash
aws s3api put-bucket-encryption \
  --bucket starterkit-attachments \
  --server-side-encryption-configuration '{
    "Rules": [{
      "ApplyServerSideEncryptionByDefault": {
        "SSEAlgorithm": "AES256"
      }
    }]
  }'
```

### 4. **Block Public Access (If Not Needed)**
```bash
aws s3api put-public-access-block \
  --bucket starterkit-attachments \
  --public-access-block-configuration \
    BlockPublicAcls=true,\
    IgnorePublicAcls=true,\
    BlockPublicPolicy=true,\
    RestrictPublicBuckets=true
```

---

## 🌐 CloudFront CDN Setup (Optional)

### Step 1: Create CloudFront Distribution
```bash
aws cloudfront create-distribution \
  --origin-domain-name starterkit-attachments.s3.us-east-1.amazonaws.com \
  --default-root-object index.html
```

### Step 2: Get Distribution Domain
```bash
aws cloudfront list-distributions --query 'DistributionList.Items[0].DomainName'
# Output: d1234567890abc.cloudfront.net
```

### Step 3: Update Configuration
```json
{
  "CloudFrontDomain": "d1234567890abc.cloudfront.net"
}
```

**Benefits:**
- ✅ Global edge caching (faster downloads)
- ✅ Reduced S3 bandwidth costs
- ✅ DDoS protection
- ✅ Custom SSL certificates

---

## 💰 Cost Optimization Tips

### 1. **Use Intelligent Tiering**
```json
{
  "StorageClass": "INTELLIGENT_TIERING"
}
```
Saves ~45% on storage costs for infrequently accessed files.

### 2. **Enable Lifecycle Policies**
```bash
# Move temp files to deletion after 2 days
aws s3api put-bucket-lifecycle-configuration \
  --bucket starterkit-attachments \
  --lifecycle-configuration '{
    "Rules": [{
      "Id": "DeleteTempFiles",
      "Filter": {"Prefix": "temp/"},
      "Status": "Enabled",
      "Expiration": {"Days": 2}
    }]
  }'
```

### 3. **Use CloudFront** (Reduces S3 bandwidth)
- Saves ~40% on data transfer costs
- Caches files at edge locations

### 4. **Enable Compression**
```csharp
uploadRequest.Metadata.Add("Content-Encoding", "gzip");
```

---

## 🧪 Testing

### Local Testing with LocalStack
```bash
# Install LocalStack
docker run -d -p 4566:4566 localstack/localstack

# Configure to use LocalStack
export AWS_ENDPOINT_URL=http://localhost:4566
```

### Create Test Bucket
```bash
aws --endpoint-url=http://localhost:4566 s3 mb s3://test-attachments
```

### Test Upload
```bash
aws --endpoint-url=http://localhost:4566 s3 cp test.jpg s3://test-attachments/
```

---

## 📊 Monitoring & Alerts

### Enable S3 CloudWatch Metrics
```bash
aws s3api put-bucket-metrics-configuration \
  --bucket starterkit-attachments \
  --id EntireBucket \
  --metrics-configuration '{
    "Id": "EntireBucket",
    "Filter": {}
  }'
```

### Monitor These Metrics
- `NumberOfObjects` - Total files
- `BucketSizeBytes` - Total storage used
- `AllRequests` - API calls
- `4xxErrors` - Client errors
- `5xxErrors` - Server errors

### Create Alerts
```bash
# Alert if bucket size > 100GB
aws cloudwatch put-metric-alarm \
  --alarm-name s3-storage-alert \
  --metric-name BucketSizeBytes \
  --threshold 107374182400  # 100GB
  --comparison-operator GreaterThanThreshold
```

---

## 🔄 Migration from Local/Azure to S3

### Option 1: AWS CLI Sync
```bash
# Sync local files to S3
aws s3 sync ./uploads/ s3://starterkit-attachments/ --acl public-read
```

### Option 2: Azure to S3 Migration
```bash
# Use rclone
rclone copy azure:container s3:starterkit-attachments --progress
```

### Option 3: Programmatic Migration
```csharp
// Copy all files from Azure to S3
var azureFiles = await azureBlobService.ListAllFilesAsync();

foreach (var file in azureFiles)
{
    var stream = await azureBlobService.DownloadAsync(file);
    await s3Service.UploadAsync(stream, file, contentType);
}
```

---

## 🐛 Troubleshooting

### Error: "Access Denied"
```
✅ Check IAM permissions
✅ Verify credentials are correct
✅ Check bucket policy
```

### Error: "NoSuchBucket"
```bash
# Create bucket if it doesn't exist
aws s3 mb s3://starterkit-attachments --region us-east-1
```

### Error: "RequestTimeout"
```csharp
// Increase timeout
var config = new AmazonS3Config
{
    Timeout = TimeSpan.FromMinutes(5),
    MaxErrorRetry = 3
};
```

### Slow Uploads
```
✅ Use TransferUtility (automatic multipart)
✅ Increase chunk size for large files
✅ Check network bandwidth
```

---

## 📚 Files Created/Modified

**New:**
- ✅ `S3StorageService.cs` - Production-grade S3 implementation

**Modified:**
- ✅ `AttachmentOptions.cs` - Added S3StorageOptions
- ✅ `StorageServiceFactory.cs` - Added S3 support
- ✅ `AttachmentModule.cs` - Register S3 client
- ✅ `Directory.Packages.props` - AWS SDK packages
- ✅ `Attachment.csproj` - AWS package references
- ✅ `appsettings.json` - S3 configuration

---

## ✅ Build Status

```bash
dotnet build src/Modules/Attachment/Attachment
# Build succeeded. ✅
```

---

## 🎯 Next Steps

1. **Create S3 Bucket**
   ```bash
   aws s3 mb s3://starterkit-attachments --region us-east-1
   ```

2. **Configure Credentials**
   ```bash
   export ATTACHMENT__STORAGE__S3__ACCESSKEYID="your-key"
   export ATTACHMENT__STORAGE__S3__SECRETACCESSKEY="your-secret"
   ```

3. **Switch Provider**
   ```json
   {
     "Attachment": {
       "Storage": {
         "Provider": "S3"
       }
     }
   }
   ```

4. **Test Upload**
   ```bash
   curl -X POST http://localhost:5000/api/v1/attachments/upload \
     -F "file=@test.jpg" \
     -F "type=Image"
   ```

5. **(Optional) Setup CloudFront CDN**
   - Create distribution
   - Point to S3 bucket
   - Update `CloudFrontDomain` config

---

## 📖 Additional Resources

- [AWS S3 Documentation](https://docs.aws.amazon.com/s3/)
- [AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/)
- [S3 Best Practices](https://docs.aws.amazon.com/AmazonS3/latest/userguide/best-practices.html)
- [CloudFront + S3](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/GettingStarted.html)

---

## 🎉 Summary

**Production-grade S3 implementation complete!**

✅ Server-side copy (fast & efficient)
✅ Batch operations (up to 1000 files)
✅ CloudFront CDN integration
✅ Server-side encryption (AES-256)
✅ Multiple storage classes
✅ Public & private file support
✅ Presigned URLs
✅ Optimized multipart uploads
✅ Comprehensive error handling
✅ Full logging

**Ready for production deployment!** 🚀

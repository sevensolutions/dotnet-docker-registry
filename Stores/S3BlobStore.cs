using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DotNetDockerRegistry.Core;
using DotNetDockerRegistry.Options;

namespace DotNetDockerRegistry.Stores;

public class S3Storage
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public S3Storage(DockerRegistryS3StorageOptions options)
    {
        _s3Client = new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, new AmazonS3Config()
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = options.ForcePathStyle
        });

        _bucketName = options.BucketName;
    }

    public async Task<GetObjectMetadataResponse> GetObjectMetadata(string key)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.GetObjectMetadataAsync(request);

        return response;
    }

    public string GetPreSignedUrl(string key)
    {
        return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest()
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.GET
        });
    }

    public async Task<GetObjectResponse> GetObject(string key)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _s3Client.GetObjectAsync(request);

        return response;
    }

    public async Task<string> PutObject(string key, Stream stream)
    {
        return await EnsureSeekableStream(stream, async stream =>
        {
            var request = new PutObjectRequest()
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = stream,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256
            };

            var response = await _s3Client.PutObjectAsync(request);

            return response.ChecksumSHA256;
        });
    }

    public async Task<S3UploadSession> BeginUpload(string uuid, string key)
    {
        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };

        var initResponse = await _s3Client.InitiateMultipartUploadAsync(initiateRequest);

        return new S3UploadSession()
        {
            Uuid = uuid,
            StorageKey = key,
            UploadId = initResponse.UploadId
        };
    }

    public async Task<PartETag> UploadPart(S3UploadSession session, Stream stream, bool isLastPart)
    {
        return await EnsureSeekableStream(stream, async stream =>
        {
            var uploadRequest = new UploadPartRequest
            {
                BucketName = _bucketName,
                Key = session.StorageKey,
                UploadId = session.UploadId,
                PartNumber = session.PartNumber,
                PartSize = stream.Length,
                InputStream = stream,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
                IsLastPart = isLastPart
            };

            var uploadResponse = await _s3Client.UploadPartAsync(uploadRequest);

            return new PartETag(uploadResponse, true);
        });
    }

    public async Task FinishUpload(S3UploadSession session)
    {
        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = session.StorageKey,
            UploadId = session.UploadId,
            PartETags = session.ETags.Select(x => new PartETag(x.PartNumber, x.ETag)
            {
                ChecksumSHA256 = x.Checksum
            }).ToList()
        };

        await _s3Client.CompleteMultipartUploadAsync(completeRequest);
    }

    public async Task AbortUpload(S3UploadSession session)
    {
        await _s3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest()
        {
            BucketName = _bucketName,
            Key = session.StorageKey,
            UploadId = session.UploadId
        });
    }

    public async Task Move(string oldKey, string newKey)
    {
        // Copy the object to the new location
        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = _bucketName,
            SourceKey = oldKey,
            DestinationBucket = _bucketName,
            DestinationKey = newKey
        };

        await _s3Client.CopyObjectAsync(copyRequest);

        // Delete the old object
        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = oldKey
        };
        await _s3Client.DeleteObjectAsync(deleteRequest);
    }

    public async Task Copy(string oldKey, string newKey)
    {
        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = _bucketName,
            SourceKey = oldKey,
            DestinationBucket = _bucketName,
            DestinationKey = newKey
        };

        await _s3Client.CopyObjectAsync(copyRequest);
    }

    private async Task<TReturn> EnsureSeekableStream<TReturn>(Stream stream, Func<Stream, Task<TReturn>> action)
    {
        string? tempFile = null;
        FileStream? tempFileStream = null;

        try
        {
            if (!stream.CanSeek)
            {
                tempFile = Path.GetTempFileName();

                tempFileStream = File.Open(tempFile, FileMode.Open);

                await stream.CopyToAsync(tempFileStream);

                tempFileStream.Seek(0, SeekOrigin.Begin);

                stream = tempFileStream;
            }

            return await action(stream);
        }
        finally
        {
            if (tempFileStream is not null)
                await tempFileStream.DisposeAsync();

            if (tempFile is not null && File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
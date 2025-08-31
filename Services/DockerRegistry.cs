using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.S3;
using DotNetDockerRegistry.Core;
using DotNetDockerRegistry.Options;
using DotNetDockerRegistry.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetDockerRegistry;

public sealed class DockerRegistry
{
    private static readonly Regex _repositoryNameRegex = new Regex(@"^[a-z0-9]+(?:[\/._-][a-z0-9]+)*$");

    private readonly ILogger<DockerRegistry> _logger;
    private readonly SessionStorage _sessionStorage;

    public DockerRegistry(ILogger<DockerRegistry> logger, IOptions<DockerRegistryOptions> options, SessionStorage sessionStorage)
    {
        _logger = logger;
        _sessionStorage = sessionStorage;

        Store = new S3Storage(options.Value.Storage.S3);
    }

    public S3Storage Store { get; }

    public bool IsValidRepositoryName(string name, out DockerApiError error)
    {
        var isValid = _repositoryNameRegex.IsMatch(name);

        if (isValid)
        {
            error = null!;
            return true;
        }

        error = new DockerApiError()
        {
            Code = DockerErrorCodes.NAME_INVALID,
            Message = "Invalid repository name",
            Detail = "The provided repository name is invalid."
        };

        return false;
    }

    public async Task<(long ContentLength, Digest Digest)?> BlobExists(string name, Digest digest)
    {
        var path = $"{name}/{digest.Hash}";

        try
        {
            var metadata = await Store.GetObjectMetadata(path);

            return (metadata.ContentLength, digest);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> GetBlobDownloadUrl(string name, Digest digest)
    {
        var path = $"{name}/{digest.Hash}";

        try
        {
            var response = await Store.GetObjectMetadata(path);

            var downloadUrl = Store.GetPreSignedUrl(path);

            return downloadUrl;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(DockerImageManifest Manifest, string RawManifest, long ContentLength, string Hash)?> GetManifest(string name, string reference)
    {
        // reference can be a digest or a tag
        var referenceWithoutPrefix = new Digest(reference).Hash; // reference.StartsWith("sha256:") ? reference.Split(":").Last() : reference;
        var path = $"{name}/{referenceWithoutPrefix}.json";

        try
        {
            using var response = await Store.GetObject(path);

            string content;

            using (var reader = new StreamReader(response.ResponseStream))
                content = reader.ReadToEnd();

            var manifestObject = JsonSerializer.Deserialize<DockerImageManifest>(content)!;

            return (
                manifestObject,
                content,
                response.ContentLength,
                response.ChecksumSHA256
            );
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IResult> BeginUpload(HttpContext context, string name)
    {
        var uuid = Guid.NewGuid().ToString();

        var session = await Store.BeginUpload(uuid, $"uploads/{uuid}");

        await _sessionStorage.SaveSessionAsync(session);

        context.Response.Headers.Location = $"/v2/{name}/blobs/uploads/{uuid}";
        context.Response.Headers.Range = "0-0";
        context.Response.Headers.ContentLength = 0;
        context.Response.Headers.Append("docker-upload-uuid", uuid);

        return Results.Accepted();
    }

    public async Task<IResult> Upload(HttpContext context, string name, string uuid)
    {
        var session = await _sessionStorage.UpdateSessionAsync(uuid, session =>
        {
            session.PartNumber++;
        });

        if (session is null)
        {
            return Results.NotFound(new DockerApiErrors(new DockerApiError()
            {
                Code = DockerErrorCodes.BLOB_UPLOAD_UNKNOWN,
                Message = "Invalid blob upload",
                Detail = "The provided blob upload UUID is unknown."
            }));
        }

        //var start = context.Request.Headers.ContentRange.FirstOrDefault()?.Split("-")[0] ?? "0";

        _logger.LogDebug($"Upload Range: {context.Request.Headers.ContentRange}");

        var etag = await Store.UploadPart(session, context.Request.Body, false);

        await _sessionStorage.UpdateSessionAsync(uuid, session =>
        {
            session.ETags.Add(new S3UploadSessionETag()
            {
                PartNumber = etag.PartNumber!.Value,
                ETag = etag.ETag,
                Checksum = etag.ChecksumSHA256
            });
        });

        context.Response.Headers.Append("docker-upload-uuid", uuid);
        context.Response.Headers.Location = $"/v2/{name}/blobs/uploads/{uuid}";
        context.Response.Headers.ContentLength = 0;
        context.Response.Headers.Append("docker-distribution-api-version", "registry/2.0");

        return Results.Accepted();
    }

    public async Task<IResult> FinishUpload(HttpContext context, string name, string uuid, Digest digest)
    {
        var session = await _sessionStorage.UpdateSessionAsync(uuid, session =>
        {
            session.PartNumber++;
        });

        if (session is null)
        {
            return Results.NotFound(new DockerApiErrors(new DockerApiError()
            {
                Code = DockerErrorCodes.BLOB_UPLOAD_UNKNOWN,
                Message = "Invalid blob upload",
                Detail = "The provided blob upload UUID is unknown."
            }));
        }

        var contentLength = context.Request.ContentLength;

        if (contentLength is not null && contentLength > 0)
        {
            var rangeHeader = context.Request.Headers.ContentRange;

            var etag = await Store.UploadPart(session, context.Request.Body, true);

            session = await _sessionStorage.UpdateSessionAsync(uuid, session =>
            {
                session.ETags.Add(new S3UploadSessionETag()
                {
                    PartNumber = etag.PartNumber!.Value,
                    ETag = etag.ETag,
                    Checksum = etag.ChecksumSHA256
                });
            });
        }

        await Store.FinishUpload(session);

        await Store.Move(session.StorageKey, $"{name}/{digest.Hash}");

        context.Response.Headers.ContentLength = 0;
        context.Response.Headers.Append("docker-content-digest", digest.ToString());

        return Results.Created($"/v2/{name}/blobs/{digest.Hash}", null);
    }

    public async Task<IResult> AbortUpload(HttpContext context, string uuid)
    {
        var session = await _sessionStorage.UpdateSessionAsync(uuid, session =>
        {
            session.PartNumber++;
        });

        if (session is null)
        {
            return Results.NotFound(new DockerApiErrors(new DockerApiError()
            {
                Code = DockerErrorCodes.BLOB_UPLOAD_UNKNOWN,
                Message = "Invalid blob upload",
                Detail = "The provided blob upload UUID is unknown."
            }));
        }

        await Store.AbortUpload(session);

        _sessionStorage.DeleteSessionAsync(session.Uuid);

        context.Response.Headers.ContentLength = 0;

        return Results.NoContent();
    }

    public async Task<IResult> SaveManifest(HttpContext context, string name, string reference)
    {
        var filePath = $"{name}/{reference}.json";

        var checksum = await Store.PutObject(filePath, context.Request.Body);

        var digest = B64Sha256ToDigest(checksum);

        context.Response.Headers.Append("docker-content-digest", digest.ToString());

        await Store.Copy(filePath, $"{name}/{digest}.json");

        return Results.Created($"/v2/{name}/manifests/{reference}", null);
    }

    public static Digest B64Sha256ToDigest(string base64Checksum)
    {
        var bytes = Convert.FromBase64String(base64Checksum);

        var sb = new StringBuilder();

        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));

        return new Digest(sb.ToString());
    }
}
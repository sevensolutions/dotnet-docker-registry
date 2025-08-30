using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DotNetDockerRegistry;

public static class DockerRegistryExtensions
{
    public static IServiceCollection AddDockerRegistry(this IServiceCollection services)
    {
        services.AddSingleton<DockerRegistry>();

        return services;
    }

    public static WebApplication UseDockerRegistry(this WebApplication application)
    {
        var registry = application.Services.GetRequiredService<DockerRegistry>();

        registry.SetupRoutes(application);

        return application;
    }
}

public sealed class DockerRegistry
{
    private ILogger<DockerRegistry> _logger;
    private readonly ConcurrentDictionary<string, S3UploadSession> _uploadSessions = new();

    public DockerRegistry(ILogger<DockerRegistry> logger)
    {
        _logger = logger;
    }

    public S3BlobStore Store { get; } = new S3BlobStore("docker");

    internal void SetupRoutes(WebApplication app)
    {
        var dockerApi = app.MapGroup("v2");

        // Exists
        // {name}/blobs/{digest}
        // {name}/manifests/{reference}
        dockerApi.MapMethods("{**path}", ["HEAD"], async (string path, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 3)
                return Results.NotFound();

            if (segments.Length >= 3 && string.Equals(segments[^2], "blobs", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                var digest = segments[^1];

                return await BlobExists(context, name, digest);
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                var reference = segments[^1];

                return await ManifestExists(context, name, reference);
            }
            else
                return Results.BadRequest();
        });

        // GetLayer / GetManifest
        // {name}/blobs/{digest}
        // {name}/manifests/{reference}
        dockerApi.MapGet("{**path}", async (string path, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 3)
                return Results.NotFound();

            if (segments.Length >= 3 && string.Equals(segments[^2], "blobs", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                var digest = segments[^1];

                return await GetBlob(context, name, digest);
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                var reference = segments[^1];

                return await GetManifest(context, name, reference);
            }
            else
                return Results.BadRequest();
        });

        // StartUpload
        // {name}/blobs/uploads
        dockerApi.MapPost("{**path}", async (string path, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 3 && string.Equals(segments[^2], "blobs", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^1], "uploads", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);
                var uuid = Guid.NewGuid().ToString();

                _logger.LogDebug("Start Upload: {0} {1}", name, uuid);

                var session = await Store.BeginUpload($"uploads/{uuid}");

                _uploadSessions.TryAdd(uuid, session);

                context.Response.Headers.Location = $"/v2/{name}/blobs/uploads/{uuid}";
                context.Response.Headers.Range = "0-0";
                context.Response.Headers.ContentLength = 0;
                context.Response.Headers.Append("docker-upload-uuid", uuid);

                return Results.Accepted();
            }
            else
                return Results.BadRequest();
        });

        // Upload
        // {name}/blobs/uploads/{uuid}
        dockerApi.MapPatch("{**path}", async (string path, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 4 && string.Equals(segments[^3], "blobs", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^2], "uploads", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^3]);
                var uuid = segments[^1];

                _logger.LogDebug("Patch Upload: {0} {1}", name, uuid);

                if (!_uploadSessions.TryGetValue(uuid, out var session))
                    return Results.NotFound();

                var start = context.Request.Headers.ContentRange.FirstOrDefault()?.Split("-")[0] ?? "0";

                await Store.UploadPart(session, context.Request.Body, false);

                context.Response.Headers.Append("docker-upload-uuid", uuid);
                context.Response.Headers.Location = $"/v2/{name}/blobs/uploads/{uuid}";
                context.Response.Headers.ContentLength = 0;
                context.Response.Headers.Append("docker-distribution-api-version", "registry/2.0");

                return Results.Accepted();
            }
            else
                return Results.BadRequest();
        });

        // FinishUpload / SaveManifest
        // {name}/blobs/uploads/{uuid}
        // {name}/manifests/{reference}
        dockerApi.MapPut("{**path}", async (string path, [FromQuery(Name = "digest")] string rawDigest, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 4 && string.Equals(segments[^3], "blobs", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^2], "uploads", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^3]);
                var uuid = segments[^1];

                _logger.LogDebug("Finish Upload: {0} {1}", name, uuid);

                return await FinishUpload(context, name, uuid, rawDigest);
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                var reference = segments[^1];

                return await SaveManifest(context, name, reference);
            }
            else
                return Results.BadRequest();
        });
    }

    private async Task<IResult> BlobExists(HttpContext context, string name, string digest)
    {
        var hash = digest.Split(":").Last();

        var path = $"{name}/{hash}";

        try
        {
            var metadata = await Store.GetObjectMetadata(path);

            context.Response.Headers.ContentLength = metadata.ContentLength;
            context.Response.Headers.Append("docker-content-digest", digest);

            return Results.Ok();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }
    }

    private async Task<IResult> ManifestExists(HttpContext context, string name, string reference)
    {
        var manifest = await TryReadManifest(name, reference);

        if (manifest is null)
            return Results.NotFound();

        var digest = ConvertChecksum(manifest.Value.Hash);

        context.Response.Headers.Append("docker-content-digest", "sha256:" + digest);
        context.Response.Headers.ContentLength = manifest.Value.Size;

        var mediaType = manifest.Value.Manifest["mediaType"].ToString();

        context.Response.Headers.ContentType = mediaType;

        return Results.Ok();
    }

    private async Task<IResult> GetBlob(HttpContext context, string name, string digest)
    {
        var hash = digest.Split(":").Last();
        var path = $"{name}/{hash}";

        try
        {
            var response = await Store.GetObjectMetadata(path);

            // context.Response.Headers.ContentLength = Store.GetSize(hash);
            // await using (var fs = Store.OpenRead(hash))
            // {
            //     await fs.CopyToAsync(context.Response.Body);
            //     return Results.Ok();
            // }

            var downloadUrl = Store.GetPreSignedUrl(path);

            return Results.Redirect(downloadUrl, false, true);
        }
        catch
        {
            return Results.NotFound();
        }
    }

    private async Task<IResult> GetManifest(HttpContext context, string name, string reference)
    {
        var manifest = await TryReadManifest(name, reference);

        if (manifest is null)
            return Results.NotFound();

        var digest = ConvertChecksum(manifest.Value.Hash);

        context.Response.Headers.Append("docker-content-digest", "sha256:" + digest);

        var mediaType = manifest.Value.Manifest["mediaType"].ToString();

        context.Response.Headers.ContentType = mediaType;
        context.Response.Headers.ContentLength = manifest.Value.Size;

        using (var response = await Store.GetObject(manifest.Value.Path))
        {
            await response.ResponseStream.CopyToAsync(context.Response.Body);
        }

        return Results.Ok();
    }

    private async Task<IResult> FinishUpload(HttpContext context, string name, string uuid, string rawDigest)
    {
        if (!_uploadSessions.TryGetValue(uuid, out var session))
            return Results.NotFound();

        var contentLength = context.Request.ContentLength;

        if (contentLength is not null && contentLength > 0)
        {
            var rangeHeader = context.Request.Headers.ContentRange;

            await Store.UploadPart(session, context.Request.Body, true);
        }

        await Store.FinishUpload(session);

        var digest = rawDigest.Split(":").Last();

        await Store.Move(session.Key, $"{name}/{digest}");

        context.Response.Headers.ContentLength = 0;
        context.Response.Headers.Append("docker-content-digest", rawDigest);

        return Results.Created($"/v2/{name}/blobs/{digest}", null);
    }
    private async Task<IResult> SaveManifest(HttpContext context, string name, string reference)
    {
        var filePath = $"{name}/{reference}.json";

        var checksum = await Store.PutObject(filePath, context.Request.Body);

        var digest = ConvertChecksum(checksum);

        context.Response.Headers.Append("docker-content-digest", "sha256:" + digest);

        await Store.Copy(filePath, $"{name}/{digest}.json");

        return Results.Created($"/v2/{name}/manifests/{reference}", null);
    }

    private static string ConvertChecksum(string base64Checksum)
    {
        var bytes = Convert.FromBase64String(base64Checksum);

        var sb = new StringBuilder();

        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }

    private async Task<(string Path, JObject Manifest, long Size, string Hash)?> TryReadManifest(string name, string reference)
    {
        var hash = reference.Split(":").Last();
        var path = $"{name}/{reference}.json";
        var hashPath = $"{name}/{hash}.json";

        GetObjectResponse? response = null;
        string? testedPath = null;

        try
        {
            try
            {
                response = await Store.GetObject(path);
                testedPath = path;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    response = await Store.GetObject(hashPath);
                    testedPath = hashPath;
                }
                catch (AmazonS3Exception e2) when (e2.StatusCode == HttpStatusCode.NotFound)
                {
                    response = null;
                }
            }

            if (response is null)
                return null;

            string content;

            using (var reader = new StreamReader(response.ResponseStream))
                content = reader.ReadToEnd();

            return (testedPath, JObject.Parse(content), response.ContentLength, response.ChecksumSHA256);
        }
        finally
        {
            response?.Dispose();
        }
    }
}

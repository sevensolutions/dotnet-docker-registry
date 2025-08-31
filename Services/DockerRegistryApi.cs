using System;
using DotNetDockerRegistry.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DotNetDockerRegistry.Services;

public sealed class DockerRegistryApi
{
    private ILogger<DockerRegistryApi> _logger;
    private readonly DockerRegistry _registry;

    public DockerRegistryApi(ILogger<DockerRegistryApi> logger, DockerRegistry registry)
    {
        _logger = logger;
        _registry = registry;
    }

    internal void SetupRoutes(WebApplication app)
    {
        var dockerApi = app.MapGroup("v2");

        // To indicate that we're supporting V2 API
        dockerApi.MapGet("", () => Results.Ok());

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

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var digest = segments[^1];

                var blobData = await _registry.BlobExists(name, new Digest(digest));

                if (blobData is not null)
                {
                    context.Response.Headers.ContentLength = blobData.Value.ContentLength;
                    context.Response.Headers.Append("docker-content-digest", digest.ToString());

                    return Results.Ok();
                }

                return Results.NotFound();
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var reference = segments[^1];

                var manifest = await _registry.GetManifest(name, reference);

                if (manifest is not null)
                {
                    var digest = DockerRegistry.B64Sha256ToDigest(manifest.Value.Hash);

                    context.Response.Headers.Append("docker-content-digest", digest.ToString());
                    context.Response.Headers.ContentLength = manifest.Value.ContentLength;

                    context.Response.Headers.ContentType = manifest.Value.Manifest.MediaType;

                    return Results.Ok();
                }

                return Results.NotFound();
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

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var digest = segments[^1];

                var downloadUrl = await _registry.GetBlobDownloadUrl(name, new Digest(digest));

                if (downloadUrl is not null)
                    return Results.Redirect(downloadUrl, false, true);

                return Results.NotFound();
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var reference = segments[^1];

                var manifest = await _registry.GetManifest(name, reference);

                if (manifest is null)
                    return Results.NotFound();

                var digest = DockerRegistry.B64Sha256ToDigest(manifest.Value.Hash);

                context.Response.Headers.Append("docker-content-digest", $"{digest}");

                context.Response.Headers.ContentLength = manifest.Value.ContentLength;

                return Results.Content(manifest.Value.RawManifest, manifest.Value.Manifest.MediaType);
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

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                return await _registry.BeginUpload(context, name);
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
                var maxSizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (maxSizeFeature is not null && !maxSizeFeature.IsReadOnly)
                    maxSizeFeature.MaxRequestBodySize = null;

                var name = string.Join('/', segments[..^3]);
                var uuid = segments[^1];

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                _logger.LogDebug("Patch Upload: {0} {1}", name, uuid);

                return await _registry.Upload(context, name, uuid);
            }
            else
                return Results.BadRequest();
        });

        // FinishUpload / SaveManifest
        // {name}/blobs/uploads/{uuid}
        // {name}/manifests/{reference}
        dockerApi.MapPut("{**path}", async (string path, [FromQuery(Name = "digest")] string? digest, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 4 && string.Equals(segments[^3], "blobs", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^2], "uploads", StringComparison.OrdinalIgnoreCase))
            {
                var maxSizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (maxSizeFeature is not null && !maxSizeFeature.IsReadOnly)
                    maxSizeFeature.MaxRequestBodySize = null;

                var name = string.Join('/', segments[..^3]);
                var uuid = segments[^1];

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                if (string.IsNullOrEmpty(digest))
                    return Results.BadRequest("Missing digest.");

                _logger.LogDebug("Finish Upload: {0} {1}", name, uuid);

                return await _registry.FinishUpload(context, name, uuid, new Digest(digest));
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var reference = segments[^1];

                return await _registry.SaveManifest(context, name, reference);
            }
            else
                return Results.BadRequest();
        });

        // Cancel Upload
        // {name}/blobs/uploads/{uuid}
        dockerApi.MapDelete("{**path}", async (string path, HttpContext context) =>
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 4 && string.Equals(segments[^3], "blobs", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^2], "uploads", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^3]);
                var uuid = segments[^1];

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                _logger.LogDebug("Cancel Upload: {0} {1}", name, uuid);

                return await _registry.AbortUpload(context, uuid);
            }
            else
                return Results.BadRequest();
        });
    }
}

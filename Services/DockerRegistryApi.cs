using System;
using DotNetDockerRegistry.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DotNetDockerRegistry;

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

                if (await _registry.BlobExists(context, name, new Digest(digest)))
                    return Results.Ok();

                return Results.NotFound();
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var reference = segments[^1];

                if (await _registry.ManifestExists(context, name, reference))
                    return Results.Ok();

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

                return await _registry.GetBlob(context, name, new Digest(digest));
            }
            else if (segments.Length >= 3 && string.Equals(segments[^2], "manifests", StringComparison.OrdinalIgnoreCase))
            {
                var name = string.Join('/', segments[..^2]);

                if (!_registry.IsValidRepositoryName(name, out var error))
                    return Results.BadRequest(new DockerApiErrors(error));

                var reference = segments[^1];

                return await _registry.GetManifest(context, name, reference);
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

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetDockerRegistry.Core;

// https://distribution.github.io/distribution/spec/manifest-v2-2/

public sealed class DockerImageManifestList
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.docker.distribution.manifest.list.v2+json";

    [JsonPropertyName("manifests")]
    public DockerImageManifestRef[] Manifests { get; set; } = default!;
}

public sealed class DockerImageManifestRef
{
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.docker.distribution.manifest.v2+json";

    [JsonPropertyName("size")]
    public int Size { get; set; } = default!;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = default!;

    [JsonPropertyName("platform")]
    public DockerImageManifestPlatform Platform { get; set; } = default!;
}

public sealed class DockerImageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.docker.distribution.manifest.v2+json";

    [JsonPropertyName("config")]
    public DockerImageManifestConfig Config { get; set; } = default!;

    [JsonPropertyName("layers")]
    public DockerImageManifestLayer[] Layers { get; set; } = default!;

    [JsonPropertyName("annotations")]
    public Dictionary<string, JsonElement>? Annotations { get; set; }
}

public sealed class DockerImageManifestConfig
{
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.docker.container.image.v1+json";

    [JsonPropertyName("size")]
    public int Size { get; set; } = default!;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = default!;
}

public sealed class DockerImageManifestPlatform
{
    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = default!;

    [JsonPropertyName("os")]
    public string OS { get; set; } = default!;

    [JsonPropertyName("os.version")]
    public string OSVersion { get; set; } = default!;

    [JsonPropertyName("os.features")]
    public string[] OSFeatures { get; set; } = default!;

    [JsonPropertyName("variant")]
    public string Variant { get; set; } = default!;

    [JsonPropertyName("features")]
    public string[] Features { get; set; } = default!;
}

public sealed class DockerImageManifestLayer
{
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.docker.image.rootfs.diff.tar.gzip";

    [JsonPropertyName("size")]
    public int Size { get; set; } = default!;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = default!;

    [JsonPropertyName("urls")]
    public string[] Urls { get; set; } = default!;
}
using System.Text.Json.Serialization;

namespace DotNetDockerRegistry;

public sealed class DockerApiErrors
{
    public DockerApiErrors()
    {
    }
    public DockerApiErrors(params DockerApiError[] errors)
    {
        Errors = errors;
    }

    [JsonPropertyName("errors")]
    public DockerApiError[] Errors { get; set; } = default!;
}

public sealed class DockerApiError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = default!;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; } = default!;
}

public static class DockerErrorCodes
{
    public const string BLOB_UNKNOWN = "BLOB_UNKNOWN";
    public const string BLOB_UPLOAD_INVALID = "BLOB_UPLOAD_INVALID";
    public const string BLOB_UPLOAD_UNKNOWN = "BLOB_UPLOAD_UNKNOWN";
    public const string DIGEST_INVALID = "DIGEST_INVALID";
    public const string MANIFEST_BLOB_UNKNOWN = "MANIFEST_BLOB_UNKNOWN";
    public const string MANIFEST_INVALID = "MANIFEST_INVALID";
    public const string MANIFEST_UNKNOWN = "MANIFEST_UNKNOWN";
    public const string MANIFEST_UNVERIFIED = "MANIFEST_UNVERIFIED";
    public const string NAME_INVALID = "NAME_INVALID";
    public const string NAME_UNKNOWN = "NAME_UNKNOWN";
    public const string SIZE_INVALID = "SIZE_INVALID";
    public const string TAG_INVALID = "TAG_INVALID";
    public const string UNAUTHORIZED = "UNAUTHORIZED";
    public const string DENIED = "DENIED";
    public const string UNSUPPORTED = "UNSUPPORTED";
}
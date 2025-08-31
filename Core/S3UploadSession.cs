using System.Collections.Generic;

namespace DotNetDockerRegistry.Core;

public sealed class S3UploadSession
{
    public string Uuid { get; set; } = default!;
    public string StorageKey { get; set; } = default!;
    public string UploadId { get; set; } = default!;
    public int PartNumber { get; set; } = default!;

    public List<S3UploadSessionETag> ETags { get; set; } = new List<S3UploadSessionETag>();
}

public sealed class S3UploadSessionETag
{
    public int PartNumber { get; set; } = default!;
    public string ETag { get; set; } = default!;
    public string Checksum { get; set; } = default!;
}
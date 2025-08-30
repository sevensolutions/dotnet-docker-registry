using System;
using System.Collections.Generic;
using System.Threading;
using Amazon.S3.Model;

namespace DotNetDockerRegistry.Core;

public sealed class S3UploadSession : IDisposable
{
    private int _partNumber = 0;

    public S3UploadSession(string uuid, string key, string uploadId)
    {
        Uuid = uuid;
        StorageKey = key;
        UploadId = uploadId;
    }

    public string Uuid { get; }
    public string StorageKey { get; set; }
    public string UploadId { get; set; }

    public List<PartETag> ETags { get; } = new List<PartETag>();

    public void Dispose()
    {
    }

    public int GetNextPartNumber()
        => Interlocked.Increment(ref _partNumber);
}
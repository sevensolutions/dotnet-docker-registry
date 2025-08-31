using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetDockerRegistry.Core;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.Caching.Distributed;

namespace DotNetDockerRegistry;

public sealed class SessionStorage
{
    private readonly IDistributedLock _lock;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(3);
    private readonly IDistributedCache _cache;

    public SessionStorage(IDistributedCache cache)
    {
        var lockfilePath = Path.GetTempFileName();

        _lock = new FileDistributedLock(new FileInfo(lockfilePath));
        _cache = cache;
    }

    public async Task SaveSessionAsync(S3UploadSession session)
    {
        await using (await _lock.AcquireAsync(LockTimeout))
        {
            var sessionJson = JsonSerializer.Serialize(session);

            await _cache.SetAsync(session.Uuid, Encoding.UTF8.GetBytes(sessionJson), new DistributedCacheEntryOptions()
            {

            });
        }
    }
    public async Task<S3UploadSession?> UpdateSessionAsync(string sessionUuid, Action<S3UploadSession>? updateFunction = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionUuid);

        await using (await _lock.AcquireAsync(LockTimeout))
        {
            var sessionJsonBinary = await _cache.GetAsync(sessionUuid);
            if (sessionJsonBinary is null)
                return null;

            var session = JsonSerializer.Deserialize<S3UploadSession>(sessionJsonBinary);
            if (session is null)
                throw new InvalidDataException("Invalid session data in cache.");

            if (updateFunction is not null)
                {
                    updateFunction.Invoke(session);

                    var sessionJson = JsonSerializer.Serialize(session);

                    await _cache.SetAsync(session.Uuid, Encoding.UTF8.GetBytes(sessionJson), new DistributedCacheEntryOptions()
                    {

                    });
                }

            return session;
        }
    }

    public void DeleteSessionAsync(string uuid)
    {
        _cache.RemoveAsync(uuid);
    }
}
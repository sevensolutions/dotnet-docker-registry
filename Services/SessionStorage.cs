using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetDockerRegistry.Core;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.Caching.Distributed;

namespace DotNetDockerRegistry.Services;

public sealed class SessionStorage
{
    private static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(3);

    private readonly IDistributedLock _lock;
    private readonly IDistributedCache _cache;
    private ConcurrentDictionary<string, DateTime> _ownSessionsLastAccess = new ConcurrentDictionary<string, DateTime>();

    public SessionStorage(IDistributedCache cache)
    {
        var lockfilePath = Path.GetTempFileName();

        _lock = new FileDistributedLock(new FileInfo(lockfilePath));
        _cache = cache;
    }

    public async Task<S3UploadSession?> GetSessionUnsafeAsync(string uuid)
    {
        var sessionJsonBinary = await _cache.GetAsync(uuid);
        if (sessionJsonBinary is null)
            return null;

        var session = JsonSerializer.Deserialize<S3UploadSession>(sessionJsonBinary);
        if (session is null)
            throw new InvalidDataException("Invalid session data in cache.");

        return session;
    }
    public async Task SaveSessionAsync(S3UploadSession session)
    {
        await using (await _lock.AcquireAsync(LockTimeout))
        {
            var sessionJson = JsonSerializer.Serialize(session);

            await _cache.SetAsync(session.Uuid, Encoding.UTF8.GetBytes(sessionJson), new DistributedCacheEntryOptions()
            {

            });

            TouchSession(session.Uuid);
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

            TouchSession(session.Uuid);

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

        _ownSessionsLastAccess.TryRemove(uuid, out _);
    }

    public IEnumerable<string> GetExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var limit = now.Subtract(MaxSessionDuration);

        foreach (var kvp in _ownSessionsLastAccess.ToArray())
        {
            if (kvp.Value < limit)
                yield return kvp.Key;
        }
    }

    private void TouchSession(string uuid)
    {
        _ownSessionsLastAccess.AddOrUpdate(uuid, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
    }
}
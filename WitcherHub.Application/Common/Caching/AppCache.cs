// File: WitcherHub.Infrastructure/Common/Caching/AppCache.cs

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Common.Caching;

namespace WitcherHub.Infrastructure.Common.Caching
{
    /// <summary>
    /// Hybrid cache:
    /// - Memory cache always
    /// - Distributed cache if registered (Redis/SQL/etc.)
    /// Includes per-key locking to prevent cache stampede.
    /// </summary>
    public sealed class AppCache : IAppCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IMemoryCache _memory;
        private readonly IDistributedCache? _distributed;
        private readonly ILogger<AppCache> _logger;

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public AppCache(
            IMemoryCache memory,
            IServiceProvider sp,
            ILogger<AppCache> logger)
        {
            _memory = memory;
            _logger = logger;

            // optional distributed cache
            _distributed = sp.GetService<IDistributedCache>();
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return default;

            if (_memory.TryGetValue(key, out T? memValue) && memValue is not null)
                return memValue;

            if (_distributed is null) return default;

            try
            {
                var json = await _distributed.GetStringAsync(key, ct);
                if (string.IsNullOrWhiteSpace(json)) return default;

                var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (value is not null)
                {
                    // small in-memory caching to reduce repeated deserialization
                    _memory.Set(key, value, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(2)
                    });
                }

                return value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache GET failed for key {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, AppCacheEntryOptions? options = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            // memory
            _memory.Set(key, value!, ToMemoryOptions(options));

            // distributed (optional)
            if (_distributed is null) return;

            try
            {
                var json = JsonSerializer.Serialize(value, JsonOptions);
                await _distributed.SetStringAsync(key, json, ToDistributedOptions(options), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache SET failed for key {Key}", key);
            }
        }

        public async Task<T> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, Task<T>> factory,
    AppCacheEntryOptions? options = null,
    CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(key, CancellationToken.None);
            if (existing is not null) return existing;

            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(CancellationToken.None);

            try
            {
                existing = await GetAsync<T>(key, CancellationToken.None);
                if (existing is not null) return existing;

                var created = await factory(CancellationToken.None);
                await SetAsync(key, created, options, CancellationToken.None);
                return created;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            _memory.Remove(key);

            if (_distributed is null) return;

            try
            {
                await _distributed.RemoveAsync(key, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache REMOVE failed for key {Key}", key);
            }
        }

        


        private static MemoryCacheEntryOptions ToMemoryOptions(AppCacheEntryOptions? o)
        {
            var opt = new MemoryCacheEntryOptions();

            if (o?.AbsoluteExpirationRelativeToNow is not null)
                opt.AbsoluteExpirationRelativeToNow = o.AbsoluteExpirationRelativeToNow;

            if (o?.SlidingExpiration is not null)
                opt.SlidingExpiration = o.SlidingExpiration;

            // reasonable default
            if (o is null || (o.AbsoluteExpirationRelativeToNow is null && o.SlidingExpiration is null))
                opt.SlidingExpiration = TimeSpan.FromMinutes(5);

            return opt;
        }

        private static DistributedCacheEntryOptions ToDistributedOptions(AppCacheEntryOptions? o)
        {
            var opt = new DistributedCacheEntryOptions();

            if (o?.AbsoluteExpirationRelativeToNow is not null)
                opt.AbsoluteExpirationRelativeToNow = o.AbsoluteExpirationRelativeToNow;

            if (o?.SlidingExpiration is not null)
                opt.SlidingExpiration = o.SlidingExpiration;

            // reasonable default
            if (o is null || (o.AbsoluteExpirationRelativeToNow is null && o.SlidingExpiration is null))
                opt.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

            return opt;
        }

        public Task<long> GetOrCreateVersionAsync(string versionKey, CancellationToken ct = default)
    => GetOrCreateAsync(
        versionKey,
        _ => Task.FromResult(1L),
        new AppCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
        ct);

        public async Task<long> BumpVersionAsync(string versionKey, CancellationToken ct = default)
        {
            var gate = _locks.GetOrAdd(versionKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);

            try
            {
                var current = await GetAsync<long>(versionKey, ct);
                var next = (current <= 0 ? 1 : current + 1);

                await SetAsync(
                    versionKey,
                    next,
                    new AppCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
                    ct);

                return next;
            }
            finally
            {
                gate.Release();
                if (gate.CurrentCount == 1)
                    _locks.TryRemove(versionKey, out _);
            }
        }
    }
}

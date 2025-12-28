
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Caching
{
    /// <summary>
    /// In-memory cache service with:
    /// - Cache-aside helper (GetOrCreateAsync)
    /// - Tags support for grouped invalidation
    /// - Post-eviction cleanup to avoid tag-index leaks
    /// - Per-key locks to prevent stampede
    /// </summary>
    public sealed class CacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<CacheService> _logger;

        // key -> tags
        private readonly ConcurrentDictionary<string, string[]> _keyTags = new(StringComparer.Ordinal);

        // tag -> (key -> byte)  (a threadsafe set)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex
            = new(StringComparer.Ordinal);

        // per-key locks for GetOrCreateAsync
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

        public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            // IMemoryCache is sync; ct is kept for API consistency
            _cache.TryGetValue(key, out T? value);
            return Task.FromResult(value);
        }

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan ttl,
            string[]? tags = null,
            CancellationToken ct = default)
        {
            try
            {
                var normalizedTags = NormalizeTags(tags);

                // update indexes first
                if (normalizedTags.Length > 0)
                {
                    _keyTags[key] = normalizedTags;

                    foreach (var tag in normalizedTags)
                    {
                        var set = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                        set[key] = 0;
                    }
                }
                else
                {
                    _keyTags.TryRemove(key, out _);
                }

                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                };

                // cleanup indexes when entry is removed/expired/evicted
                options.RegisterPostEvictionCallback((k, v, reason, state) =>
                {
                    try
                    {
                        CleanupKeyIndexes(k?.ToString());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Cache eviction cleanup failed for key={Key}", k);
                    }
                });

                _cache.Set(key, value!, options);
            }
            catch (Exception ex)
            {
                // caching must never break app flow
                _logger.LogWarning(ex, "Cache Set failed for key={Key}", key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            try
            {
                _cache.Remove(key);
                CleanupKeyIndexes(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache Remove failed for key={Key}", key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveByTagAsync(string tag, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Task.CompletedTask;

            try
            {
                if (_tagIndex.TryRemove(tag, out var keysSet))
                {
                    foreach (var key in keysSet.Keys)
                    {
                        _cache.Remove(key);
                        CleanupKeyIndexes(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache RemoveByTag failed for tag={Tag}", tag);
            }

            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan ttl,
            string[]? tags = null,
            CancellationToken ct = default)
        {
            // fast path
            if (_cache.TryGetValue(key, out T? cached) && cached is not null)
                return cached;

            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(ct);
            try
            {
                // double-check after lock
                if (_cache.TryGetValue(key, out cached) && cached is not null)
                    return cached;

                var created = await factory(ct);

                // do not cache null by default (you can change that rule if you want)
                if (created is not null)
                    await SetAsync(key, created, ttl, tags, ct);

                return created!;
            }
            finally
            {
                gate.Release();

                // optional: cleanup lock object to prevent unbounded growth
                // safe best-effort
                if (gate.CurrentCount == 1)
                    _locks.TryRemove(key, out _);
            }
        }

        private static string[] NormalizeTags(string[]? tags)
        {
            if (tags is null || tags.Length == 0) return Array.Empty<string>();

            return tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private void CleanupKeyIndexes(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (_keyTags.TryRemove(key, out var tags))
            {
                foreach (var tag in tags)
                {
                    if (_tagIndex.TryGetValue(tag, out var set))
                    {
                        set.TryRemove(key, out _);

                        // if tag is empty remove it
                        if (set.IsEmpty)
                            _tagIndex.TryRemove(tag, out _);
                    }
                }
            }
        }
    }
}

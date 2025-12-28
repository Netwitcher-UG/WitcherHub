namespace WitcherHub.Application.Common.Caching
{
    public interface IAppCache
    {
        Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

        Task SetAsync<T>(
            string key,
            T value,
            AppCacheEntryOptions? options = null,
            CancellationToken ct = default);

        Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            AppCacheEntryOptions? options = null,
            CancellationToken ct = default);

        Task RemoveAsync(string key, CancellationToken ct = default);

        // Version keys (soft invalidation)
        Task<long> GetOrCreateVersionAsync(string versionKey, CancellationToken ct = default);
        Task<long> BumpVersionAsync(string versionKey, CancellationToken ct = default);
    }

    public sealed class AppCacheEntryOptions
    {
        public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
        public TimeSpan? SlidingExpiration { get; set; }
    }
}

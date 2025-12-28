
namespace WitcherHub.Application.Interfaces
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

        Task SetAsync<T>(
            string key,
            T value,
            TimeSpan ttl,
            string[]? tags = null,
            CancellationToken ct = default);

        Task RemoveAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Removes all cached entries that were stored with the given tag.
        /// Useful for invalidating "customers:list" after mutations.
        /// </summary>
        Task RemoveByTagAsync(string tag, CancellationToken ct = default);

        /// <summary>
        /// Cache-aside helper. If cache miss -> create -> store -> return.
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan ttl,
            string[]? tags = null,
            CancellationToken ct = default);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class CustomerCacheKeys
    {
        private const string Prefix = "customers";

        // Version key (soft invalidation for all list caches)
        public const string ListVersionKey = Prefix + ":list:version";

        public static string Details(Guid customerId)
            => $"{Prefix}:details:{customerId:D}";

        /// <summary>
        /// Base list key (without version).
        /// </summary>
        public static string List(int page, int pageSize, string? search)
        {
            var searchHash = SearchHash(search);
            return $"{Prefix}:list:p={page}:ps={pageSize}:q={searchHash}";
        }

        /// <summary>
        /// List key with version (recommended to use always).
        /// </summary>
        public static string ListWithVersion(int page, int pageSize, string? search, long version)
            => $"{List(page, pageSize, search)}:v={version}";

        // -----------------------
        // Helpers
        // -----------------------
        public static string SearchHash(string? search)
        {
            var s = NormalizeSearch(search);
            if (string.IsNullOrWhiteSpace(s)) return "none";
            return ShortHash(s);
        }

        private static string NormalizeSearch(string? search)
            => string.IsNullOrWhiteSpace(search) ? "" : search.Trim().ToLowerInvariant();

        private static string ShortHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        }
    }
}

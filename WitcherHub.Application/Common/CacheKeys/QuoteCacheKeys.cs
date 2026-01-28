using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class QuoteCacheKeys
    {
        private const string Prefix = "quotes";
        public const string ListVersionKey = Prefix + ":list:version";

        public static string Details(Guid quoteId)
            => $"{Prefix}:details:{quoteId:D}";

        public static string ListByProject(int page, int pageSize, Guid projectId, string? search)
        {
            var q = SearchHash(search);
            return $"{Prefix}:list:project={projectId:D}:p={page}:ps={pageSize}:q={q}";
        }

        public static string ListByProjectWithVersion(int page, int pageSize, Guid projectId, string? search, long version)
            => $"{ListByProject(page, pageSize, projectId, search)}:v={version}";

        private static string SearchHash(string? search)
        {
            var s = string.IsNullOrWhiteSpace(search) ? "" : search.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s)) return "none";

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        }
    }
}

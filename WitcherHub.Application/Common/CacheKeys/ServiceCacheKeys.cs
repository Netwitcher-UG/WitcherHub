using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class ServiceCacheKeys
    {
        public const string ListVersionKey = "services:list:ver";

        public static string ListWithVersion(int page, int pageSize, string? search, long version)
            => $"services:list:v{version}:p{page}:s{pageSize}:q{(search ?? "").Trim().ToLowerInvariant()}";

        public static string Details(Guid id) => $"services:details:{id}";
    }
}

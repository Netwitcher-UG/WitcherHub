using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class ProjectCacheKeys
    {
        public const string ListVersionKey = "projects:list:ver";

        public static string ListWithVersion(int page, int pageSize, string? search, string? customerName, string? status, long version)
            => $"projects:list:v{version}:p{page}:s{pageSize}:q{search}:c{customerName}:st{status}";

        public static string Details(Guid id) => $"projects:details:{id}";
    }
}

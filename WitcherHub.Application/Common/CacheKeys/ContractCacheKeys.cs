using System;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class ContractCacheKeys
    {
        public const string ListVersionKey = "contracts:list:version";

        public static string ListByProjectWithVersion(int page, int pageSize, Guid projectId, string? search, long version)
            => $"contracts:list:proj:{projectId}:p:{page}:ps:{pageSize}:q:{(search ?? "")}:v:{version}";

        public static string Details(Guid contractId)
            => $"contracts:details:{contractId}";
    }
}

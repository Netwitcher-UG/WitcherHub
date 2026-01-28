using System;

namespace WitcherHub.Application.Common.CacheKeys
{
    public static class InvoiceCacheKeys
    {
        public const string ListVersionKey = "invoices:list:version";

        public static string ListByProjectWithVersion(int page, int pageSize, Guid projectId, string? search, long version)
            => $"invoices:list:proj:{projectId}:p:{page}:ps:{pageSize}:q:{(search ?? "")}:v:{version}";

        public static string Details(Guid invoiceId)
            => $"invoices:details:{invoiceId}";
    }
}

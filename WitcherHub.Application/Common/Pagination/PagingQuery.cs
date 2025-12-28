namespace WitcherHub.Application.Common.Pagination
{
    public sealed class PagingQuery
    {
        // 1-based page index
        public int Page { get; init; } = 1;

        // per-page
        public int PageSize { get; init; } = 10;

        // optional search
        public string? Search { get; init; }

        // optional sort
        // مثال: "name", "type", "city", "email"
        public string? SortBy { get; init; }

        // asc/desc
        public bool Desc { get; init; } = false;

        // لضمان قيم سليمة
        public PagingQuery Normalize(int maxPageSize = 100)
        {
            var page = Page < 1 ? 1 : Page;
            var size = PageSize < 1 ? 10 : PageSize;
            if (size > maxPageSize) size = maxPageSize;

            return new PagingQuery
            {
                Page = page,
                PageSize = size,
                Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                SortBy = string.IsNullOrWhiteSpace(SortBy) ? null : SortBy.Trim(),
                Desc = Desc
            };
        }
    }
}

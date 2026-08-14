using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// Paging links that preserve whatever filter is in the query string.
    ///
    /// Built from the live request rather than from a list of known parameters, so
    /// adding a filter to a register never means remembering to thread it through
    /// the pager as well — which is how paging quietly dropped the search term on
    /// the older list pages.
    /// </summary>
    public sealed class PagerVm
    {
        public required int Page { get; init; }
        public required int PageSize { get; init; }
        public required long TotalItems { get; init; }
        public required string BasePath { get; init; }

        /// <summary>The current query string, minus the page number.</summary>
        public required IReadOnlyDictionary<string, string?> QueryWithoutPage { get; init; }

        public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalItems + PageSize - 1) / PageSize);
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;

        public long FromItem => TotalItems == 0 ? 0 : ((long)(Page - 1) * PageSize) + 1;
        public long ToItem => Math.Min((long)Page * PageSize, TotalItems);

        public static PagerVm From(HttpRequest request, int page, int pageSize, long totalItems)
        {
            var query = request.Query
                .Where(kv => !string.Equals(kv.Key, "page", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());

            return new PagerVm
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                BasePath = request.Path,
                QueryWithoutPage = query
            };
        }

        public string UrlForPage(int page)
        {
            var query = new Dictionary<string, string?>(QueryWithoutPage) { ["page"] = page.ToString() };
            return QueryHelpers.AddQueryString(BasePath, query);
        }

        /// <summary>
        /// The page numbers to render: the first, the last, and a window around the
        /// current one. <c>null</c> stands for a gap. Twenty pages of numbers is
        /// not navigation, it is wallpaper.
        /// </summary>
        public IEnumerable<int?> PageNumbers()
        {
            const int window = 2;
            var last = TotalPages;

            var previous = 0;

            for (var i = 1; i <= last; i++)
            {
                var isEdge = i == 1 || i == last;
                var isNear = Math.Abs(i - Page) <= window;

                if (!isEdge && !isNear) continue;

                if (previous != 0 && i - previous > 1)
                    yield return null;

                yield return i;
                previous = i;
            }
        }
    }
}

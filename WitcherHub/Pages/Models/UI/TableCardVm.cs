using Microsoft.AspNetCore.Html;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// A table with a search box and pagination.
    ///
    /// It no longer carries a page title or a primary action: those belong to
    /// <see cref="PageHeaderVm"/>, and having them here as well meant a page either
    /// showed two titles or relied on this one and so had no header of its own.
    /// </summary>
    public class TableCardVm
    {
        public string SearchPlaceholder { get; set; } = "Search";

        public List<TableColumnVm> Columns { get; set; } = new();
        public List<TableRowVm> Rows { get; set; } = new();
        public PaginationVm? Pagination { get; set; }
    }

    public class TableColumnVm
    {
        public string Header { get; set; } = "";
        public string? HeaderClass { get; set; }
        public string? CellClass { get; set; }
        public string? Width { get; set; }
    }

    public class TableRowVm
    {
        public List<IHtmlContent> Cells { get; set; } = new();
        public string? RowClass { get; set; }
    }


    // =======================
    // ✅ Pagination ViewModels
    // =======================

    public class PaginationVm
    {
        // current page (1-based)
        public int Page { get; set; } = 1;

        // items per page
        public int PageSize { get; set; } = 10;

        // total items count in DB
        public long TotalItems { get; set; }

        // query string param names (optional but clean)
        public string PageParam { get; set; } = "page";
        public string PageSizeParam { get; set; } = "pageSize";

        // for preserving search/filter query string
        public string? SearchQuery { get; set; }
        public string SearchParam { get; set; } = "q";

        public int TotalPages
        {
            get
            {
                if (PageSize <= 0) return 0;
                return (int)Math.Ceiling(TotalItems / (double)PageSize);
            }
        }

        public bool HasPages => TotalPages > 1;
        public bool HasPrev => Page > 1;
        public bool HasNext => Page < TotalPages;

        public int PrevPage => Math.Max(1, Page - 1);
        public int NextPage => Math.Min(TotalPages, Page + 1);

        /// <summary>
        /// Builds pagination items: 1 ... (window) ... N
        /// </summary>
        public IReadOnlyList<PageItemVm> BuildItems(int window = 2)
        {
            var items = new List<PageItemVm>();

            if (TotalPages <= 0) return items;

            // Always include first
            items.Add(PageItemVm.Number(1, Page == 1));

            var start = Math.Max(2, Page - window);
            var end = Math.Min(TotalPages - 1, Page + window);

            if (start > 2)
                items.Add(PageItemVm.Ellipsis());

            for (var p = start; p <= end; p++)
                items.Add(PageItemVm.Number(p, p == Page));

            if (end < TotalPages - 1)
                items.Add(PageItemVm.Ellipsis());

            // Always include last (if > 1)
            if (TotalPages > 1)
                items.Add(PageItemVm.Number(TotalPages, Page == TotalPages));

            return items;
        }
    }

    public class PageItemVm
    {
        public string Text { get; private set; } = "";
        public int? Page { get; private set; }     // null => ellipsis
        public bool IsActive { get; private set; }
        public bool IsDisabled { get; private set; }
        public bool IsEllipsis => Page == null;

        private PageItemVm() { }

        public static PageItemVm Number(int page, bool active)
            => new PageItemVm
            {
                Text = page.ToString(),
                Page = page,
                IsActive = active
            };

        public static PageItemVm Ellipsis()
            => new PageItemVm
            {
                Text = "…",
                Page = null,
                IsDisabled = true
            };
    }
}

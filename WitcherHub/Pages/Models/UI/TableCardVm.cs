using Microsoft.AspNetCore.Html;

namespace WitcherHub.Pages.Models.UI
{
    public class TableCardVm
    {
        public string Title { get; set; } = "";
        public string SearchPlaceholder { get; set; } = "Search";

        public string? PrimaryButtonText { get; set; }
        public string? PrimaryButtonTarget { get; set; } 

        public List<TableColumnVm> Columns { get; set; } = new();
        public List<TableRowVm> Rows { get; set; } = new();
    }

    public class TableColumnVm
    {
        public string Header { get; set; } = "";
        public string? HeaderClass { get; set; }
        public string? CellClass { get; set; }
    }

    public class TableRowVm
    {
        public List<IHtmlContent> Cells { get; set; } = new();
    }
}

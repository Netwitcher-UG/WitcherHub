using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Registers;
using WitcherHub.Domain.SeedData;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    /// <summary>
    /// Every invoice, and what is still owed on it.
    ///
    /// The question this page exists to answer — "who owes us money" — previously
    /// had no query behind it anywhere in the application.
    /// </summary>
    [Authorize(Policy = AppPolicyPrefixes.Permission + AppPermissions.ManageNetwitcher)]
    public class IndexModel : PageModel
    {
        private readonly IDocumentRegister _register;

        public IndexModel(IDocumentRegister register) => _register = register;

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public int? Status { get; set; }
        [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
        [BindProperty(SupportsGet = true)] public bool OutstandingOnly { get; set; }
        [BindProperty(SupportsGet = true)] public bool OverdueOnly { get; set; }
        [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;

        public PagedResult<InvoiceRegisterRow> Results { get; private set; } =
            PagedResult<InvoiceRegisterRow>.Empty(1, 20);

        public RegisterFilterVm Filters { get; private set; } = default!;
        public PagerVm Pager { get; private set; } = default!;

        /// <summary>
        /// Totals for the rows on this page. Labelled as such in the view — a
        /// figure that silently covers only one page of results is worse than no
        /// figure at all. The business-wide totals live on the dashboard.
        /// </summary>
        public decimal PageBilled { get; private set; }
        public decimal PageOutstanding { get; private set; }
        public int PageOverdueCount { get; private set; }

        public async Task OnGetAsync(CancellationToken ct)
        {
            var status = Status is null ? (DocumentStatus?)null : (DocumentStatus)Status.Value;

            var filter = new RegisterFilter
            {
                Search = Search,
                Status = status,
                CustomerId = CustomerId,
                OutstandingOnly = OutstandingOnly,
                OverdueOnly = OverdueOnly,
                Page = Page < 1 ? 1 : Page,
                PageSize = 20
            };

            Results = await _register.GetInvoicesAsync(filter, ct);

            PageBilled = Results.Items.Sum(r => r.Total);
            PageOutstanding = Results.Items.Sum(r => r.BalanceDue);
            PageOverdueCount = Results.Items.Count(r => r.IsOverdue);

            Filters = new RegisterFilterVm
            {
                Search = Search ?? "",
                Status = status,
                CustomerId = CustomerId,
                SearchPlaceholder = "Invoice number, customer or project…",
                ShowMoneyFilters = true,
                OutstandingOnly = OutstandingOnly,
                OverdueOnly = OverdueOnly,
                StatusOptions =
                [
                    (DocumentStatus.Draft, "Draft"),
                    (DocumentStatus.Issued, "Issued"),
                    (DocumentStatus.Sent, "Sent"),
                    (DocumentStatus.Open, "Open"),
                    (DocumentStatus.Overdue, "Overdue"),
                    (DocumentStatus.Paid, "Paid"),
                    (DocumentStatus.Cancelled, "Cancelled")
                ],
                Customers = await _register.GetCustomersWithDocumentsAsync(DocumentKind.Invoice, ct)
            };

            Pager = PagerVm.From(Request, Results.Page, Results.PageSize, Results.TotalItems);
        }
    }
}

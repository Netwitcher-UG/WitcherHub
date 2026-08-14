using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Registers;
using WitcherHub.Domain.SeedData;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes
{
    /// <summary>
    /// Every quote in the business.
    ///
    /// Until now a quote could only be reached by opening the customer, then the
    /// project, then the quotes tab — which requires already knowing where it is.
    /// </summary>
    [Authorize(Policy = AppPolicyPrefixes.Permission + AppPermissions.ManageNetwitcher)]
    public class IndexModel : PageModel
    {
        private readonly IDocumentRegister _register;

        public IndexModel(IDocumentRegister register) => _register = register;

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public int? Status { get; set; }
        [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
        [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;

        public PagedResult<QuoteRegisterRow> Results { get; private set; } =
            PagedResult<QuoteRegisterRow>.Empty(1, 20);

        public RegisterFilterVm Filters { get; private set; } = default!;
        public PagerVm Pager { get; private set; } = default!;

        /// <summary>Totals for the rows the filter selected, not for the page shown.</summary>
        public decimal AwaitingValue { get; private set; }
        public int AwaitingCount { get; private set; }

        public async Task OnGetAsync(CancellationToken ct)
        {
            var status = Status is null ? (DocumentStatus?)null : (DocumentStatus)Status.Value;

            var filter = new RegisterFilter
            {
                Search = Search,
                Status = status,
                CustomerId = CustomerId,
                Page = Page < 1 ? 1 : Page,
                PageSize = 20
            };

            Results = await _register.GetQuotesAsync(filter, ct);

            AwaitingCount = Results.Items.Count(r => r.Status == DocumentStatus.Sent);
            AwaitingValue = Results.Items.Where(r => r.Status == DocumentStatus.Sent).Sum(r => r.ItemsTotal);

            Filters = new RegisterFilterVm
            {
                Search = Search ?? "",
                Status = status,
                CustomerId = CustomerId,
                SearchPlaceholder = "Quote number, customer or project…",
                StatusOptions =
                [
                    (DocumentStatus.Draft, "Draft"),
                    (DocumentStatus.Sent, "Awaiting customer"),
                    (DocumentStatus.Accepted, "Accepted"),
                    (DocumentStatus.Signed, "Signed"),
                    (DocumentStatus.Rejected, "Rejected"),
                    (DocumentStatus.Cancelled, "Cancelled")
                ],
                Customers = await _register.GetCustomersWithDocumentsAsync(DocumentKind.Quote, ct)
            };

            Pager = PagerVm.From(Request, Results.Page, Results.PageSize, Results.TotalItems);
        }
    }
}

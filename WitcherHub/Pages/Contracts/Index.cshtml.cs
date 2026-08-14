using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Registers;
using WitcherHub.Domain.SeedData;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    /// <summary>
    /// Every contract in the business, with the two things that need watching:
    /// which are still waiting on a signature, and which are about to end.
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

        public PagedResult<ContractRegisterRow> Results { get; private set; } =
            PagedResult<ContractRegisterRow>.Empty(1, 20);

        public RegisterFilterVm Filters { get; private set; } = default!;
        public PagerVm Pager { get; private set; } = default!;

        public int AwaitingSignatureCount { get; private set; }

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

            Results = await _register.GetContractsAsync(filter, ct);
            AwaitingSignatureCount = Results.Items.Count(r => r.Status == DocumentStatus.Sent);

            Filters = new RegisterFilterVm
            {
                Search = Search ?? "",
                Status = status,
                CustomerId = CustomerId,
                SearchPlaceholder = "Contract number, customer or project…",
                StatusOptions =
                [
                    (DocumentStatus.Draft, "Draft"),
                    (DocumentStatus.Sent, "Awaiting signature"),
                    (DocumentStatus.Signed, "Signed"),
                    (DocumentStatus.Accepted, "Accepted"),
                    (DocumentStatus.Rejected, "Rejected"),
                    (DocumentStatus.Terminated, "Terminated"),
                    (DocumentStatus.Cancelled, "Cancelled")
                ],
                Customers = await _register.GetCustomersWithDocumentsAsync(DocumentKind.Contract, ct)
            };

            Pager = PagerVm.From(Request, Results.Page, Results.PageSize, Results.TotalItems);
        }

        /// <summary>
        /// Days until a contract ends, when that is inside the next two months.
        /// Null otherwise, so the view only draws attention where there is some.
        /// </summary>
        public static int? DaysUntilEnd(DateOnly? endDate)
        {
            if (endDate is null) return null;

            var days = endDate.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
            return days is >= 0 and <= 60 ? days : null;
        }
    }
}

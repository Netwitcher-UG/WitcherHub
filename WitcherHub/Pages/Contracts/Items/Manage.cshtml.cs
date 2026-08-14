using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Contracts.Items
{
    /// <summary>
    /// Superseded by the contract builder at /Contracts/Positions/{contractId}.
    ///
    /// This page was the one the contract-creation flow actually landed on, and it
    /// could only add positions that already existed in the Service Catalog — its
    /// empty state read "Click From Services to create the first one", with no way
    /// to type a position by hand. The builder does both, so this route forwards
    /// there rather than offering a second, worse editor.
    ///
    /// Kept as a redirect rather than deleted: the project workspace, older
    /// browser history and any bookmark still point here.
    /// </summary>
    [Authorize]
    public class ManageModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public Guid ContractId { get; set; }

        public IActionResult OnGet()
        {
            if (ContractId == Guid.Empty)
                return RedirectToPage("/Contracts/Index");

            return RedirectToPage("/Contracts/Positions", new { contractId = ContractId });
        }
    }
}

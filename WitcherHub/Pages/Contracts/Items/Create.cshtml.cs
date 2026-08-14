using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Contracts.Items
{
    /// <summary>
    /// Superseded by the contract builder at /Contracts/Positions/{contractId}.
    ///
    /// "Add Position" here required a Service Catalog entry to be selected before
    /// anything could be entered, and offered no price field at all — the price
    /// came from the chosen service. The builder accepts positions typed by hand,
    /// with their own price, and needs no catalog entry.
    /// </summary>
    [Authorize]
    public class CreateModel : PageModel
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

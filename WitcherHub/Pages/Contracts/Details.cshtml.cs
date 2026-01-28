using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Contracts;

namespace WitcherHub.Pages.Contracts
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly IContract _contracts;

        public DetailsModel(IContract contracts)
        {
            _contracts = contracts;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(Id, ct);
                if (Contract is null)
                    throw new NotFoundAppException("Contract not found.");

                return Page();
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Bad request";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects");
            }
            catch (NotFoundAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not found";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects");
            }
        }
    }
}
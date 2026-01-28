using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Invoices;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly IInvoice _invoices;

        public DetailsModel(IInvoice invoices)
        {
            _invoices = invoices;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid invoice id.");

                Invoice = await _invoices.GetInvoiceAsync(Id, ct);
                if (Invoice is null)
                    throw new NotFoundAppException("Invoice not found.");

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
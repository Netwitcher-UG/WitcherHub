using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Invoices;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    [AllowAnonymous]
    public class DetailsModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly LexwareClient _lex;

        public DetailsModel(IInvoice invoices, LexwareClient lex)
        {
            _invoices = invoices;
            _lex = lex;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return NotFound();

            Invoice = await _invoices.GetInvoiceAsync(Id, ct);
            if (Invoice is null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(Invoice.LexwareInvoiceId))
                return RedirectToPage("./Details", "Pdf", new { id = Id });

            return Page();
        }

        public async Task<IActionResult> OnGetPdfAsync(bool download = false, CancellationToken ct = default)
        {
            if (Id == Guid.Empty)
                return NotFound();

            var inv = await _invoices.GetInvoiceAsync(Id, ct);
            if (inv is null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(inv.LexwareInvoiceId))
                return Content("Invoice is not linked to Lexware.", "text/plain");

            if (inv.Status == DocumentStatus.Draft ||
                string.Equals(inv.LexwareVoucherStatus, "draft", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(409, "PDF is not available yet because the invoice is still draft.");
            }

            try
            {
                var pdfBytes = await _lex.DownloadInvoiceFileAsync(
                    inv.LexwareInvoiceId!,
                    "application/pdf",
                    ct);

                var downloadName = $"Invoice-{(string.IsNullOrWhiteSpace(inv.InvoiceNo) ? inv.Id.ToString() : inv.InvoiceNo)}.pdf";

                FileContentResult result;

                if (download)
                    result = File(pdfBytes, "application/pdf", downloadName);
                else
                    result = File(pdfBytes, "application/pdf");

                result.EnableRangeProcessing = true;

                Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
                Response.Headers["Pragma"] = "no-cache";

                return result;
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(409, $"PDF is not available from Lexware yet. {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(502, $"Failed to fetch invoice PDF from Lexware. {ex.GetBaseException().Message}");
            }
        }
    }
}

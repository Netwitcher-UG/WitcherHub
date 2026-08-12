using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    // Internal viewer: streams the invoice PDF for any invoice id, so it must be
    // authenticated. The customer-facing route is /Invoices/View, which requires
    // a signed, expiring access token.
    public class PdfViewerModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly LexwareClient _lex;

        public PdfViewerModel(IInvoice invoices, LexwareClient lex)
        {
            _invoices = invoices;
            _lex = lex;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public async Task<IActionResult> OnGetAsync(bool download = false, CancellationToken ct = default)
        {
            if (Id == Guid.Empty)
                return Content("Invalid invoice id.", "text/plain");

            var inv = await _invoices.GetInvoiceAsync(Id, ct);
            if (inv is null)
                return NotFound("Invoice not found.");

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

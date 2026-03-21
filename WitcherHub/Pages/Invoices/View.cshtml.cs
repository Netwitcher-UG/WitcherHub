using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Infrastructure.Services.Invoices;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Public.Invoices
{
    [AllowAnonymous]
    public class ViewModel : PageModel
    {
        private readonly InvoicePublicLinkService _publicLinks;
        private readonly LexwareClient _lex;

        public ViewModel(
            InvoicePublicLinkService publicLinks,
            LexwareClient lex)
        {
            _publicLinks = publicLinks;
            _lex = lex;
        }

        public async Task<IActionResult> OnGetAsync(string t, bool download = false, CancellationToken ct = default)
        {
            var link = await _publicLinks.ValidateActiveLinkAsync(t, ct);
            if (link is null)
                return NotFound("This invoice link is invalid or expired.");

            var inv = link.Invoice;
            if (inv is null)
                return NotFound("Invoice not found.");

            if (string.IsNullOrWhiteSpace(inv.LexwareInvoiceId))
                return NotFound("Invoice is not linked to Lexware.");

            if (inv.Status == DocumentStatus.Draft ||
                string.Equals(inv.LexwareVoucherStatus, "draft", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(409, "Invoice PDF is not available yet because the invoice is still draft.");
            }

            try
            {
                var pdfBytes = await _lex.DownloadInvoiceFileAsync(
                    inv.LexwareInvoiceId!,
                    "application/pdf",
                    ct);

                await _publicLinks.MarkOpenedAsync(link.Id, ct);

                var fileName = $"Invoice-{(string.IsNullOrWhiteSpace(inv.InvoiceNo) ? inv.Id.ToString() : inv.InvoiceNo)}.pdf";

                FileContentResult result;

                if (download)
                    result = File(pdfBytes, "application/pdf", fileName);
                else
                    result = File(pdfBytes, "application/pdf");

                result.EnableRangeProcessing = true;

                Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                Response.Headers["Referrer-Policy"] = "no-referrer";

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

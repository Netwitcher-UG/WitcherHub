using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Invoices;
using WitcherHub.Domain.SeedData;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    /// <summary>
    /// The invoice, as the business sees it: what was billed, what has been
    /// received, and what is still owed.
    ///
    /// This page used to redirect straight to the Lexware PDF whenever the invoice
    /// had been exported, which meant an exported invoice had no page inside
    /// WitcherHub at all — its items, totals and payments could not be looked at,
    /// and a payment could not be recorded because there was nowhere to record it.
    /// The PDF is now a button instead of a redirect; the handler that serves it is
    /// unchanged.
    ///
    /// Internal page: it exposes invoice data by id with no access token, so it
    /// must stay authenticated. The customer-facing route is /Invoices/View, which
    /// validates an expiring token.
    /// </summary>
    [Authorize(Policy = AppPolicyPrefixes.Permission + AppPermissions.ManageNetwitcher)]
    public class DetailsModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IPayments _payments;
        private readonly LexwareClient _lex;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(
            IInvoice invoices,
            IPayments payments,
            LexwareClient lex,
            ILogger<DetailsModel> logger)
        {
            _invoices = invoices;
            _payments = payments;
            _lex = lex;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }
        public IReadOnlyList<PaymentRow> Payments { get; private set; } = [];

        /// <summary>The PDF only exists once the invoice has been exported and left draft.</summary>
        public bool HasLexwarePdf =>
            Invoice is not null &&
            !string.IsNullOrWhiteSpace(Invoice.LexwareInvoiceId) &&
            Invoice.Status != DocumentStatus.Draft &&
            !string.Equals(Invoice.LexwareVoucherStatus, "draft", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A payment can only be recorded against an invoice that has been issued
        /// and still has a balance.
        /// </summary>
        public bool CanRecordPayment =>
            Invoice is not null &&
            Invoice.Status is not (DocumentStatus.Draft or DocumentStatus.Void or DocumentStatus.Cancelled) &&
            (Invoice.Totals?.BalanceDue ?? 1m) > 0m;

        public int? DaysOverdue => Models.UI.Format.DaysOverdue(Invoice?.DueDate);

        public bool IsOverdue =>
            DaysOverdue is not null &&
            Invoice is not null &&
            Invoice.Status != DocumentStatus.Paid &&
            (Invoice.Totals?.BalanceDue ?? 0m) > 0m;

        // ---- the payment form ------------------------------------------------
        [BindProperty] public decimal PaymentAmount { get; set; }
        [BindProperty] public DateOnly PaymentReceivedOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
        [BindProperty] public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Bank;
        [BindProperty] public string? PaymentReference { get; set; }
        [BindProperty] public bool PaymentIsPending { get; set; }

        public string? ErrorMessage { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return NotFound();

            if (!await LoadAsync(ct))
                return NotFound();

            // Offer the outstanding balance as the default, which is what is
            // actually being paid nine times out of ten.
            PaymentAmount = Invoice!.Totals?.BalanceDue ?? 0m;

            return Page();
        }

        public async Task<IActionResult> OnPostRecordPaymentAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return NotFound();

            try
            {
                var result = await _payments.RecordAsync(new RecordPaymentDto
                {
                    InvoiceId = Id,
                    Amount = PaymentAmount,
                    ReceivedOn = PaymentReceivedOn,
                    Method = PaymentMethod,
                    Reference = PaymentReference,
                    Status = PaymentIsPending ? PaymentStatus.Pending : PaymentStatus.Success
                }, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = result.InvoiceIsNowPaid ? "Invoice paid" : "Payment recorded";
                TempData["Toast.Message"] = result.InvoiceIsNowPaid
                    ? "The balance is now clear and the invoice is marked as paid."
                    : $"{Models.UI.Format.Money(result.BalanceDue)} still outstanding.";

                return RedirectToPage(new { id = Id });
            }
            catch (AppException ex)
            {
                // A rejected payment is a normal outcome — a draft invoice, a
                // future date, a zero amount — so it belongs on the page rather
                // than on the error handler.
                ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record a payment against invoice {InvoiceId}.", Id);
                ErrorMessage = "The payment could not be recorded. The details are in the application log.";
            }

            if (!await LoadAsync(ct))
                return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostDeletePaymentAsync(Guid paymentId, CancellationToken ct)
        {
            if (Id == Guid.Empty || paymentId == Guid.Empty)
                return NotFound();

            try
            {
                var result = await _payments.DeleteAsync(paymentId, ct);

                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Payment removed";
                TempData["Toast.Message"] = $"{Models.UI.Format.Money(result.BalanceDue)} outstanding.";
            }
            catch (AppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Could not remove the payment";
                TempData["Toast.Message"] = ex.Message;
            }

            return RedirectToPage(new { id = Id });
        }

        private async Task<bool> LoadAsync(CancellationToken ct)
        {
            Invoice = await _invoices.GetInvoiceAsync(Id, ct);
            if (Invoice is null) return false;

            Payments = await _payments.GetForInvoiceAsync(Id, ct);
            return true;
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

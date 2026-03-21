
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Invoices
{
    public interface IInvoiceNotificationService
    {
        Task QueueInvoiceReadyEmailAsync(Guid localInvoiceId, CancellationToken ct = default);
    }

    public sealed class InvoiceNotificationService : IInvoiceNotificationService
    {
        private readonly AppDbContext _db;
        private readonly LexwareClient _lex;
        private readonly IEmailService _emailService;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly ILogger<InvoiceNotificationService> _logger;

        public InvoiceNotificationService(
            AppDbContext db,
            LexwareClient lex,
            IEmailService emailService,
            IEmailTemplateRenderer renderer,
            ILogger<InvoiceNotificationService> logger)
        {
            _db = db;
            _lex = lex;
            _emailService = emailService;
            _renderer = renderer;
            _logger = logger;
        }

        public async Task QueueInvoiceReadyEmailAsync(Guid localInvoiceId, CancellationToken ct = default)
        {
            var invoice = await _db.Invoices
                .Include(x => x.Totals)
                .Include(x => x.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(c => c.Contacts)
                .Include(x => x.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(c => c.EmailAddresses)
                .FirstOrDefaultAsync(x => x.Id == localInvoiceId, ct);

            if (invoice is null)
                throw new InvalidOperationException("Invoice not found.");

            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                throw new InvalidOperationException("Invoice has no Lexware reference.");

            if (invoice.Status == DocumentStatus.Draft ||
                string.Equals(invoice.LexwareVoucherStatus, "draft", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invoice PDF is not available yet because the invoice is still draft.");
            }

            var recipientEmail =
                invoice.Project?.Customer?.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => (c.Email ?? "").Trim())
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                recipientEmail =
                    invoice.Project?.Customer?.EmailAddresses?
                        .OrderByDescending(ea => (ea.Kind ?? "").Trim().Equals("business", StringComparison.OrdinalIgnoreCase))
                        .Select(ea => (ea.Email ?? "").Trim())
                        .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
            }

            if (string.IsNullOrWhiteSpace(recipientEmail))
                throw new InvalidOperationException("Customer email was not found.");

            var recipientName =
                invoice.Project?.Customer?.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => c.Name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? invoice.Project?.Customer?.Name
                ?? "Kunde";

            var pdfBytes = await _lex.DownloadInvoiceFileAsync(
                invoice.LexwareInvoiceId!,
                "application/pdf",
                ct);

            var invoiceNo = FirstNotEmpty(
                invoice.InvoiceNo,
                invoice.LexwareVoucherNumber,
                invoice.LexwareInvoiceId,
                invoice.Id.ToString());

            var issueDateText = invoice.IssueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-";
            var dueDateText = invoice.DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-";
            var totalText = invoice.Totals?.Total.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";
            var currencyText = string.IsNullOrWhiteSpace(invoice.Currency) ? "EUR" : invoice.Currency;

            var subject = $"Ihre Rechnung {invoiceNo}";

            var model = new
            {
                Subject = subject,
                UserName = recipientName,
                InvoiceNo = invoiceNo,
                ProjectTitle = invoice.Project?.Title ?? "-",
                IssueDate = issueDateText,
                DueDate = dueDateText,
                Total = totalText,
                Currency = currencyText
            };

            var html = await _renderer.RenderAsync("InvoiceReady", model, ct);

            var message = new EmailMessage
            {
                From = new EmailAddress("no-reply@invalid.local", "WitcherHub"),
                Subject = subject,
                HtmlBody = html,
                TextBody = $"Guten Tag {recipientName}, im Anhang finden Sie Ihre Rechnung {invoiceNo} als PDF.",
                To =
                [
                    new EmailAddress(recipientEmail, recipientName)
                ],
                Attachments =
                [
                    new EmailAttachment(
                        $"Rechnung-{SanitizeFileName(invoiceNo)}.pdf",
                        "application/pdf",
                        pdfBytes)
                ]
            };

            await _emailService.QueueNowAsync(message, ct);

            _logger.LogInformation(
                "Invoice notification queued. InvoiceId={InvoiceId}, Recipient={Recipient}",
                invoice.Id,
                recipientEmail);
        }

        private static string FirstNotEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "-";

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(clean) ? "invoice" : clean;
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.Email;
using WitcherHub.Configuration.Filters;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Controllers
{
    [Route("invoices/test-notifications")]
    [DevelopmentOnly]
    public sealed class InvoiceTestNotificationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LexwareClient _lex;
        private readonly IEmailSender _emailSender;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly IWebHostEnvironment _env;

        public InvoiceTestNotificationsController(
            AppDbContext db,
            LexwareClient lex,
            IEmailSender emailSender,
            IEmailTemplateRenderer renderer,
            IWebHostEnvironment env)
        {
            _db = db;
            _lex = lex;
            _emailSender = emailSender;
            _renderer = renderer;
            _env = env;
        }

        // POST /invoices/test-notifications/send?invoiceId=...
        [HttpPost("send")]
        public async Task<IActionResult> Send(CancellationToken ct)
        {
            if (!_env.IsDevelopment())
                return NotFound();

            const string testEmail = "basel.slaby@gmail.com";
            const string testName = "Test Receiver";

            var invoice = await _db.Invoices
    .Include(x => x.Totals)
    .Include(x => x.Project)
        .ThenInclude(p => p.Customer)
    .Where(x =>
        x.LexwareInvoiceId != null &&
        x.LexwareInvoiceId != "" &&
        x.Status != DocumentStatus.Draft &&
        (x.LexwareVoucherStatus == null || !EF.Functions.ILike(x.LexwareVoucherStatus, "draft")))
    .OrderByDescending(x => x.CreatedAt)
    .FirstOrDefaultAsync(ct);

            if (invoice is null)
                return NotFound(new { ok = false, message = "No suitable invoice found for test sending." });

            var pdfBytes = await _lex.DownloadInvoiceFileAsync(
                invoice.LexwareInvoiceId!,
                "application/pdf",
                ct);

            var invoiceNo =
                !string.IsNullOrWhiteSpace(invoice.InvoiceNo) ? invoice.InvoiceNo :
                !string.IsNullOrWhiteSpace(invoice.LexwareVoucherNumber) ? invoice.LexwareVoucherNumber :
                invoice.Id.ToString();

            var subject = $"Ihre Rechnung {invoiceNo}";

            var html = await _renderer.RenderAsync(
                templateName: "InvoiceReady",
                model: new
                {
                    Subject = subject,
                    UserName = testName,
                    InvoiceNo = invoiceNo,
                    ProjectTitle = invoice.Project?.Title ?? "-",
                    IssueDate = invoice.IssueDate?.ToString("dd.MM.yyyy") ?? "-",
                    DueDate = invoice.DueDate?.ToString("dd.MM.yyyy") ?? "-",
                    Total = invoice.Totals?.Total.ToString("0.00") ?? "-",
                    Currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "EUR" : invoice.Currency
                },
                ct);

            var msg = new EmailMessage
            {
                From = new EmailAddress("placeholder@local", "WitcherHub"),
                Subject = subject,
                HtmlBody = html,
                TextBody = $"Im Anhang finden Sie Ihre Rechnung {invoiceNo} als PDF.",
                Bcc = new List<EmailAddress>
        {
            new EmailAddress(testEmail, testName)
        },
                Attachments = new List<EmailAttachment>
        {
            new EmailAttachment(
                $"Rechnung-{invoiceNo}.pdf",
                "application/pdf",
                pdfBytes)
        }
            };

            await _emailSender.SendAsync(msg, ct);

            return Ok(new
            {
                ok = true,
                message = $"Test invoice email sent to {testEmail}.",
                invoiceId = invoice.Id,
                lexwareInvoiceId = invoice.LexwareInvoiceId,
                invoiceNo
            });
        }


    }
}
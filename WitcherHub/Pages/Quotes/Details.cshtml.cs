using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Pdf;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly IQuote _quotes;
        private readonly AppDbContext _db;
        private readonly IPdfGenerator _pdf;
        private readonly IEmailService _email;
        private readonly WitcherHub.Application.Interfaces.Email.IEmailTemplateRenderer _emailRenderer;
        private readonly IConfiguration _cfg;

        public DetailsModel(
            IQuote quotes,
            AppDbContext db,
            IPdfGenerator pdf,
            IEmailService email,
            WitcherHub.Application.Interfaces.Email.IEmailTemplateRenderer emailRenderer,
            IConfiguration cfg)
        {
            _quotes = quotes;
            _db = db;
            _pdf = pdf;
            _email = email;
            _emailRenderer = emailRenderer;
            _cfg = cfg;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public WitcherHub.Application.Models.View.Quotes.QuoteViews.QuoteDetailsView? Quote { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null)
                    throw new NotFoundAppException("Quote not found.");

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

        // =========================
        // GET: PDF
        // =========================
        public async Task<IActionResult> OnGetPdfAsync(CancellationToken ct)
        {
            try
            {
                var model = await LoadPdfModelAsync(ct);

                var html = QuotePdfHtmlBuilder.Build(model);
                var bytes = _pdf.FromHtml(html, $"Angebot {model.QuoteNo}");

                var fileName = $"{model.QuoteNo}.pdf";
                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("./Details", new { id = Id });
            }
        }

        // =========================
        // POST: Send quote to customer (email + pdf attachment)
        // =========================
        public async Task<IActionResult> OnPostSendAsync(CancellationToken ct)
        {
            try
            {
                var model = await LoadPdfModelAsync(ct);

                if (string.IsNullOrWhiteSpace(model.Customer.Email))
                    throw new BadRequestAppException("Customer email not found for this project.");

                // PDF
                var pdfHtml = QuotePdfHtmlBuilder.Build(model);
                var pdfBytes = _pdf.FromHtml(pdfHtml, $"Angebot {model.QuoteNo}");

                // Public base URL (for optional link)
                var publicBaseUrl =
                    _cfg["WITCHERHUB_PUBLIC_BASE_URL"]
                    ?? Environment.GetEnvironmentVariable("WITCHERHUB_PUBLIC_BASE_URL")
                    ?? "";
                publicBaseUrl = NormalizeBaseUrl(publicBaseUrl);

                var actionUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
                    ? ""
                    : $"{publicBaseUrl}/Quotes/Details?id={model.QuoteId}";

                var actionBlock = "";

                // Email template render
                var subject = $"Angebot {model.QuoteNo}";

                var emailHtml = await _emailRenderer.RenderAsync(
                    "QuoteSent",
                    new
                    {
                        Subject = subject,
                        UserName = model.Customer.DisplayName,
                        QuoteNo = model.QuoteNo,
                        ProjectTitle = model.ProjectTitle,
                        ExpiresAt = model.ExpiresAt?.ToString("dd.MM.yyyy") ?? "—",
                        ActionBlock = actionBlock
                    },
                    ct);

                var msg = new EmailMessage
                {
                    From = new EmailAddress("no-reply@invalid.local", "WitcherHub"),
                    Subject = subject,
                    HtmlBody = emailHtml,
                    TextBody = null,
                    To = [],
                    Bcc = [new EmailAddress(model.Customer.Email!, model.Customer.DisplayName)],
                    Attachments =
                    [
                        new EmailAttachment($"{model.QuoteNo}.pdf", "application/pdf", pdfBytes)
                    ]
                };

                await _email.QueueNowAsync(msg, ct);

                // (اختياري وآمن) حدّث الحالة إلى Sent فقط لو كانت Draft
                if (model.StatusText?.Equals(DocumentStatus.Draft.ToString(), StringComparison.OrdinalIgnoreCase) == true)
                {
                    var dto = new UpdateQuoteDto
                    {
                        Quote = new QuoteDto
                        {
                            ProjectId = model.ProjectId,
                            Currency = model.Currency,
                            Notes = model.Notes,
                            IssuedAt = model.IssuedAt ?? DateTimeOffset.Now,
                            ExpiresAt = model.ExpiresAt,
                            Status = DocumentStatus.Sent
                        },
                        Items = null
                    };

                    await _quotes.UpdateAsync(model.QuoteId, dto, ct);
                }

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Sent";
                TempData["Toast.Message"] = "Quote sent to customer with PDF attached.";

                return RedirectToPage("./Details", new { id = Id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("./Details", new { id = Id });
            }
        }

        // =========================
        // Helpers
        // =========================
        private async Task<QuotePdfHtmlBuilder.QuotePdfDocumentModel> LoadPdfModelAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                throw new BadRequestAppException("Invalid quote id.");

            var q = await _quotes.GetQuoteAsync(Id, ct);
            if (q is null)
                throw new NotFoundAppException("Quote not found.");

            var proj = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Id == q.ProjectId)
                .Select(p => new { p.Id, p.Title, p.CustomerId })
                .FirstOrDefaultAsync(ct);

            if (proj is null)
                throw new NotFoundAppException("Project not found.");

            var cust = await _db.Customers
                .AsNoTracking()
                .Where(c => c.Id == proj.CustomerId)
                .Select(c => new { c.Id, c.Name, c.FirstName, c.LastName, c.Type })
                .FirstOrDefaultAsync(ct);

            if (cust is null)
                throw new NotFoundAppException("Customer not found.");

            var addr = await _db.CustomerAddresses
                .AsNoTracking()
                .Where(a => a.CustomerId == cust.Id)
                .OrderByDescending(a => a.IsDefault ? 1 : 0)
                .ThenByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.StreetRaw,
                    a.PostalCode,
                    a.City,
                    a.Country
                })
                .FirstOrDefaultAsync(ct);

            // Email: EmailAddresses أولاً
            var email = await _db.CustomerEmailAddresses
                .AsNoTracking()
                .Where(e => e.CustomerId == cust.Id)
                .OrderByDescending(e => (e.Kind ?? "").ToLower() == "primary" ? 1 : 0)
                .ThenByDescending(e => e.CreatedAt)
                .Select(e => e.Email)
                .FirstOrDefaultAsync(ct);

            // fallback: Contacts
            if (string.IsNullOrWhiteSpace(email))
            {
                email = await _db.CustomerContacts
                    .AsNoTracking()
                    .Where(x => x.CustomerId == cust.Id && x.Email != null && x.Email != "")
                    .OrderByDescending(x => x.IsPrimary ? 1 : 0)
                    .ThenByDescending(x => x.CreatedAt)
                    .Select(x => x.Email)
                    .FirstOrDefaultAsync(ct);
            }

            // ✅ اسم العميل فقط من Customer
            var isIndividual = cust.Type.ToString().Equals("Individual", StringComparison.OrdinalIgnoreCase);

            var personName = BuildName(cust.FirstName, cust.LastName, cust.Name);
            var companyName = (cust.Name ?? "").Trim();

            string? pdfCompanyName = null;
            string pdfDisplayName = "";

            if (isIndividual)
            {
                pdfCompanyName = null;
                pdfDisplayName = personName;
            }
            else
            {
                pdfCompanyName = companyName;
                pdfDisplayName = ""; // ✅ لا نعرض سطر ثاني
            }

            var lines = new List<QuotePdfHtmlBuilder.QuotePdfLine>();
            decimal sumSub = 0m, sumDisc = 0m, sumTax = 0m, sumTotal = 0m;

            foreach (var it in (q.Items ?? []).OrderBy(x => x.Position))
            {
                var baseTotal = it.Quantity * it.UnitPrice;

                var sub = ReadDec(it.PriceBreakdown, "subTotal", baseTotal);
                var disc = ReadNestedDec(it.PriceBreakdown, "discount", "amount", 0m);
                var tax = ReadNestedDec(it.PriceBreakdown, "tax", "amount", 0m);
                var total = ReadDec(it.PriceBreakdown, "total", Math.Max(0m, sub) + Math.Max(0m, tax));

                lines.Add(new QuotePdfHtmlBuilder.QuotePdfLine
                {
                    Position = it.Position,
                    Title = it.Title ?? "",
                    ServiceName = it.ServiceName,
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice,
                    SubTotal = sub,
                    Discount = disc,
                    Tax = tax,
                    Total = total
                });

                sumSub += sub;
                sumDisc += disc;
                sumTax += tax;
                sumTotal += total;
            }

            var vatPercent = sumTax > 0m ? 19m : 0m;

            return new QuotePdfHtmlBuilder.QuotePdfDocumentModel
            {
                QuoteId = q.Id,
                ProjectId = q.ProjectId,
                QuoteNo = q.QuoteNo,
                Currency = q.Currency ?? "EUR",
                StatusText = q.Status.ToString(),
                CreatedAt = q.CreatedAt,
                IssuedAt = q.IssuedAt,
                ExpiresAt = q.ExpiresAt,
                Notes = q.Notes,
                ProjectTitle = proj.Title ?? "",

                Customer = new QuotePdfHtmlBuilder.QuotePdfCustomer
                {
                    CompanyName = pdfCompanyName,
                    DisplayName = pdfDisplayName,
                    Street = addr?.StreetRaw,
                    PostalCode = addr?.PostalCode,
                    City = addr?.City,
                    Country = addr?.Country,
                    Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim()
                },

                Lines = lines,
                Totals = new QuotePdfHtmlBuilder.QuotePdfTotals
                {
                    SubTotal = sumSub,
                    Discount = sumDisc,
                    Tax = sumTax,
                    Total = sumTotal,
                    VatPercent = vatPercent
                }
            };
        }
        private static string NormalizeBaseUrl(string baseUrl)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return "";

            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            return baseUrl.TrimEnd('/');
        }

        private static string BuildName(string? first, string? last, string? fallback)
        {
            var f = (first ?? "").Trim();
            var l = (last ?? "").Trim();
            var both = (f + " " + l).Trim();
            return string.IsNullOrWhiteSpace(both) ? (fallback ?? "").Trim() : both;
        }

        private static decimal ReadDec(JsonDocument? doc, string prop, decimal fallback = 0m)
        {
            try
            {
                if (doc is null) return fallback;
                var root = doc.RootElement;
                if (!root.TryGetProperty(prop, out var v)) return fallback;

                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;

                return fallback;
            }
            catch { return fallback; }
        }

        private static decimal ReadNestedDec(JsonDocument? doc, string parent, string prop, decimal fallback = 0m)
        {
            try
            {
                if (doc is null) return fallback;
                var root = doc.RootElement;
                if (!root.TryGetProperty(parent, out var p)) return fallback;
                if (p.ValueKind != JsonValueKind.Object) return fallback;
                if (!p.TryGetProperty(prop, out var v)) return fallback;

                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;

                return fallback;
            }
            catch { return fallback; }
        }
    }
}
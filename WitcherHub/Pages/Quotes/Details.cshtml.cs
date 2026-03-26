using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Infrastructure.Services.Pdf;
using WitcherHub.Infrastructure.Services.Quotes;
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
        private readonly IEmailTemplateRenderer _emailRenderer;
        private readonly IConfiguration _cfg;
        private readonly QuotePublicLinkService _quotePublicLinkService;
                private readonly LexwareInvoiceSyncService _lexwareInvoiceSyncService;

        public DetailsModel(
                IQuote quotes,
                AppDbContext db,
                IPdfGenerator pdf,
                IEmailService email,
                IEmailTemplateRenderer emailRenderer,
                IConfiguration cfg,
                     LexwareInvoiceSyncService lexwareInvoiceSyncService,
                QuotePublicLinkService quotePublicLinkService)
        {
            _quotes = quotes;
            _db = db;
            _pdf = pdf;
            _email = email;
            _emailRenderer = emailRenderer;
            _lexwareInvoiceSyncService = lexwareInvoiceSyncService;
            _cfg = cfg;
            _quotePublicLinkService = quotePublicLinkService;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public WitcherHub.Application.Models.View.Quotes.QuoteViews.QuoteDetailsView? Quote { get; private set; }
        public bool HasSignedQuotePdf { get; private set; }
  
        public bool ShowManualInvoiceButton { get; private set; }
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null)
                    throw new NotFoundAppException("Quote not found.");

                HasSignedQuotePdf = await _db.QuoteSignatures
                    .AsNoTracking()
                    .Where(x => x.QuoteId == Id && x.SignedAt != null && x.SignatureData != null)
                    .AnyAsync(ct);

                await LoadQuoteStateAsync(ct);

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
        public async Task<IActionResult> OnPostGenerateInvoiceAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid quote id.");

                var quote = await _db.Quotes
                    .Include(q => q.Items)
                    .FirstOrDefaultAsync(q => q.Id == Id, ct);

                if (quote is null)
                    throw new NotFoundAppException("Quote not found.");

                var isSigned =
                    quote.Status == DocumentStatus.Signed ||
                    await _db.QuoteSignatures
                        .AsNoTracking()
                        .AnyAsync(x => x.QuoteId == quote.Id && x.SignedAt != null, ct);

                if (!isSigned)
                    throw new BadRequestAppException("Invoice can only be generated after the quote is signed.");

                if (quote.InvoiceSendMode != InvoiceSendMode.Manual)
                    throw new BadRequestAppException("Manual invoice generation is available only for quotes with Manual invoice mode.");

                if (quote.Items == null || quote.Items.Count == 0)
                    throw new BadRequestAppException("Please add at least one Position first.");

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasOneTimeItems = quote.Items.Any(i => i.BillingCycle == BillingCycle.OneTime);
                var hasRecurringItems = quote.Items.Any(i => i.BillingCycle != BillingCycle.OneTime);

                var results = new List<InvoiceGenerationResult>();

                if (hasRecurringItems)
                {
                    var start =
                        quote.RecurringStartDate ??
                        (quote.SignedAt.HasValue
                            ? DateOnly.FromDateTime(quote.SignedAt.Value.UtcDateTime)
                            : quote.IssuedAt.HasValue
                                ? DateOnly.FromDateTime(quote.IssuedAt.Value.UtcDateTime)
                                : today);

                    quote.RecurringEnabled = true;
                    quote.RecurringIsActive = true;

                    if (quote.NextRecurringInvoiceDate == null)
                        quote.NextRecurringInvoiceDate = start;

                    await _db.SaveChangesAsync(ct);
                }

                if (hasOneTimeItems)
                {
                    results.Add(await _lexwareInvoiceSyncService.CreateOneTimeInvoiceFromQuoteAsync(quote.Id, ct));
                }

                if (hasRecurringItems)
                {
                    if (!quote.NextRecurringInvoiceDate.HasValue)
                    {
                        results.Add(InvoiceGenerationResult.Warning("Recurring start date is missing."));
                    }
                    else if (quote.NextRecurringInvoiceDate.Value > today)
                    {
                        results.Add(InvoiceGenerationResult.Warning(
                            $"Recurring invoice is not due yet. Next cycle date is {quote.NextRecurringInvoiceDate.Value:yyyy-MM-dd}."));
                    }
                    else
                    {
                        while (quote.NextRecurringInvoiceDate.HasValue &&
                               quote.NextRecurringInvoiceDate.Value <= today)
                        {
                            var recurringResult =
                                await _lexwareInvoiceSyncService.CreateRecurringInvoiceFromQuoteAsync(
                                    quote.Id,
                                    quote.NextRecurringInvoiceDate.Value,
                                    ct);

                            results.Add(recurringResult);

                            if (!recurringResult.Created)
                                break;

                            await _db.Entry(quote).ReloadAsync(ct);
                        }
                    }
                }

                var createdCount = results.Count(r => r.Created);
                var message = string.Join(" ",
                    results.Select(r => r.Message)
                           .Where(m => !string.IsNullOrWhiteSpace(m))
                           .Distinct());

                if (createdCount > 0)
                {
                    TempData["Toast.Type"] = "success";
                    TempData["Toast.Title"] = "Done";
                    TempData["Toast.Message"] = message;
                }
                else
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Invoice not created";
                    TempData["Toast.Message"] = string.IsNullOrWhiteSpace(message)
                        ? "No invoice was created."
                        : message;
                }
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Invoice not created";
                TempData["Toast.Message"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Invoice failed";
                TempData["Toast.Message"] = ex.GetBaseException().Message;
            }

            return RedirectToPage("./Details", new { id = Id });
        }
        private async Task LoadQuoteStateAsync(CancellationToken ct)
        {
            var quote = await _db.Quotes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == Id, ct);

            if (quote is null)
            {
                ShowManualInvoiceButton = false;
                return;
            }

            var isSigned =
                quote.Status == DocumentStatus.Signed ||
                await _db.QuoteSignatures
                    .AsNoTracking()
                    .AnyAsync(x => x.QuoteId == quote.Id && x.SignedAt != null, ct);

            ShowManualInvoiceButton =
                isSigned &&
                quote.InvoiceSendMode == InvoiceSendMode.Manual;
        }
        public async Task<IActionResult> OnGetSignedPdfAsync(CancellationToken ct)
        {
            try
            {
                var sig = await _db.QuoteSignatures
                    .AsNoTracking()
                    .Where(x => x.QuoteId == Id && x.SignedAt != null)
                    .OrderByDescending(x => x.SignedAt)
                    .FirstOrDefaultAsync(ct);

                if (sig is null)
                    throw new BadRequestAppException("Signed quote PDF is not available.");

                string? signatureDataUrl = null;

                if (sig.SignatureData is not null &&
                    sig.SignatureData.RootElement.TryGetProperty("dataUrl", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    signatureDataUrl = p.GetString();
                }

                if (string.IsNullOrWhiteSpace(signatureDataUrl))
                    throw new BadRequestAppException("Signed quote PDF is not available.");

                var model = await LoadPdfModelAsync(ct);

                var html = BuildSignedQuotePdfHtml(
                    model,
                    sig.SignerName,
                    sig.SignerEmail,
                    sig.SignedAt ?? DateTimeOffset.UtcNow,
                    signatureDataUrl);

                var bytes = _pdf.FromHtml(html, $"Angebot {model.QuoteNo} - signed");

                return File(bytes, "application/pdf", $"{model.QuoteNo}-signed.pdf");
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

                if (string.Equals(model.StatusText, DocumentStatus.Signed.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new BadRequestAppException("This quote is already signed.");

                // PDF attachment (unsigned)
                var pdfHtml = QuotePdfHtmlBuilder.Build(model);
                var pdfBytes = _pdf.FromHtml(pdfHtml, $"Angebot {model.QuoteNo}");

                // Create public signing link
                var rawToken = await _quotePublicLinkService.CreateAsync(
                    model.QuoteId,
                    model.Customer.Email,
                    expiresInDays: 14,
                    ct: ct);

                var publicBaseUrl =
                    _cfg["WITCHERHUB_PUBLIC_BASE_URL"]
                    ?? Environment.GetEnvironmentVariable("WITCHERHUB_PUBLIC_BASE_URL")
                    ?? $"{Request.Scheme}://{Request.Host}";

                publicBaseUrl = NormalizeBaseUrl(publicBaseUrl);

                var actionUrl = $"{publicBaseUrl}/quotes/sign/{model.QuoteId}?t={Uri.EscapeDataString(rawToken)}";

                var subject = $"Angebot {model.QuoteNo} zur Prüfung und Unterschrift";

                var emailHtml = await _emailRenderer.RenderAsync(
                    "QuoteSignatureRequest",
                    new
                    {
                        Subject = subject,
                        UserName = model.Customer.DisplayName,
                        QuoteNo = model.QuoteNo,
                        ProjectTitle = model.ProjectTitle,
                        ExpiresAt = model.ExpiresAt?.ToString("dd.MM.yyyy") ?? "—",
                        ActionUrl = actionUrl
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

                var currentQuote = await _quotes.GetQuoteAsync(Id, ct);
                if (currentQuote is null)
                    throw new NotFoundAppException("Quote not found.");

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
                            Status = DocumentStatus.Sent,
                            ApplyVat = currentQuote.ApplyVat,
                            AfterCustomerSignAction = currentQuote.AfterCustomerSignAction,
                            InvoiceSendMode = currentQuote.InvoiceSendMode
                        },
                        Items = null
                    };

                    await _quotes.UpdateAsync(model.QuoteId, dto, ct);
                }

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Sent";
                TempData["Toast.Message"] = "Quote email with PDF and signing link has been sent.";

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

        public async Task<IActionResult> OnPostCreatePublicLinkAsync(CancellationToken ct)
        {
            try
            {
                var model = await LoadPdfModelAsync(ct);

                if (string.Equals(model.StatusText, DocumentStatus.Signed.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new BadRequestAppException("This quote is already signed.");

                if (model.Lines is null || model.Lines.Count == 0)
                    throw new BadRequestAppException("Please add at least one Position before creating a public link.");

                if (string.IsNullOrWhiteSpace(model.Customer.Email))
                    throw new BadRequestAppException("Customer email not found for this quote.");

                var rawToken = await _quotePublicLinkService.CreateAsync(
                    model.QuoteId,
                    model.Customer.Email,
                    expiresInDays: 14,
                    ct: ct);

                var publicUrl = Url.Page(
                    "/Quotes/Sign",
                    pageHandler: null,
                    values: new { id = model.QuoteId, t = rawToken },
                    protocol: Request.Scheme,
                    host: Request.Host.ToUriComponent());

                if (string.IsNullOrWhiteSpace(publicUrl))
                    throw new InvalidOperationException("Failed to build public link.");

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        url = publicUrl,
                        expiresAt = DateTimeOffset.UtcNow.AddDays(14),
                        recipientEmail = model.Customer.Email
                    }
                });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = ex.Message
                })
                { StatusCode = 400 };
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = ex.GetBaseException().Message
                })
                { StatusCode = 500 };
            }
        }
        // =========================
        // Helpers
        // =========================

        private static string MapBillingCycleText(BillingCycle billingCycle)
        {
            return billingCycle switch
            {
                BillingCycle.OneTime => "Einmalig",
                BillingCycle.Monthly => "Monatlich",
                BillingCycle.Quarterly => "Vierteljährlich",
                BillingCycle.SemiAnnual => "Halbjährlich",
                BillingCycle.Annual => "Jährlich",
                _ => billingCycle.ToString()
            };
        }

        private static string BuildDiscountDisplay(DiscountType? discountType, decimal? discountValue)
        {
            if (discountType is null || discountValue is null || discountValue.Value <= 0m)
                return "—";

            var de = CultureInfo.GetCultureInfo("de-DE");

            return discountType switch
            {
                DiscountType.Percent => discountValue.Value.ToString("0.##", de) + " %",
                DiscountType.Fixed => discountValue.Value.ToString("N2", de) + " €",
                DiscountType.Amount => discountValue.Value.ToString("N2", de) + " €",
                _ => discountValue.Value.ToString("0.##", de)
            };
        }
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

            var email = await _db.CustomerEmailAddresses
                .AsNoTracking()
                .Where(e => e.CustomerId == cust.Id)
                .OrderByDescending(e => (e.Kind ?? "").ToLower() == "primary" ? 1 : 0)
                .ThenByDescending(e => e.CreatedAt)
                .Select(e => e.Email)
                .FirstOrDefaultAsync(ct);

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
                pdfDisplayName = "";
            }

            var vatPercent = q.ApplyVat ? 19m : 0m;

            var lines = new List<QuotePdfHtmlBuilder.QuotePdfLine>();

            decimal sumBase = 0m;
            decimal sumDisc = 0m;
            decimal sumNet = 0m;
            decimal sumTax = 0m;
            decimal sumTotal = 0m;

            foreach (var it in (q.Items ?? []).OrderBy(x => x.Position))
            {
                var fallbackBase = it.Quantity * it.UnitPrice;

                var baseTotal = ReadDec(it.PriceBreakdown, "baseTotal", fallbackBase);
                var disc = ReadNestedDec(it.PriceBreakdown, "discount", "amount", 0m);
                var sub = ReadDec(it.PriceBreakdown, "subTotal", Math.Max(0m, baseTotal - disc));
                var total = ReadDec(it.PriceBreakdown, "total", sub);

                if (total <= 0m)
                    total = sub > 0m ? sub : Math.Max(0m, baseTotal - disc);

                lines.Add(new QuotePdfHtmlBuilder.QuotePdfLine
                {
                    Position = it.Position,
                    Title = it.Title ?? "",
                    Description = string.IsNullOrWhiteSpace(it.Description) ? null : it.Description.Trim(),
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice,
                    BillingCycleText = MapBillingCycleText(it.BillingCycle),
                    VatPercent = vatPercent,
                    DiscountDisplay = BuildDiscountDisplay(it.DiscountType, it.DiscountValue),
                    SubTotal = baseTotal,
                    Discount = disc,
                    Tax = 0m,
                    Total = total
                });

                sumBase += baseTotal;
                sumDisc += disc;
                sumNet += total;
            }

            sumTax = q.ApplyVat ? Math.Max(0m, sumNet * 0.19m) : 0m;
            sumTotal = sumNet + sumTax;

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
                    SubTotal = sumBase,
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

        private static string BuildSignedQuotePdfHtml(
    QuotePdfHtmlBuilder.QuotePdfDocumentModel model,
    string signerName,
    string signerEmail,
    DateTimeOffset signedAt,
    string signatureDataUrl)
        {
            var html = QuotePdfHtmlBuilder.Build(model);

            var extraStyle = """
<style>
  .signedQuoteBlock{
    margin: 24px 0 0 0;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .signedQuoteCard{
    border: 1px solid #d7c7f3;
    border-radius: 18px;
    padding: 18px 20px;
    background: linear-gradient(180deg, #ffffff, #faf5ff);
  }

  .signedQuoteTitle{
    font-size: 18px;
    font-weight: 800;
    color: #2e1065;
    margin: 0 0 14px 0;
  }

  .signedQuoteRow{
    margin: 6px 0;
    font-size: 12.5px;
    line-height: 1.6;
    color: #31263f;
  }

  .signedQuoteRow strong{
    display: inline-block;
    min-width: 120px;
    color: #6b21a8;
  }

  .signedQuoteImage{
    margin-top: 14px;
  }

  .signedQuoteImage img{
    max-width: 260px;
    max-height: 120px;
    display: block;
  }

  .signedQuoteLine{
    width: 260px;
    border-top: 1px solid #7c3aed;
    margin-top: 8px;
  }
</style>
""";

            var signatureBlock = $"""
<div class="signedQuoteBlock">
  <div class="signedQuoteCard">
    <h2 class="signedQuoteTitle">Kundenunterschrift</h2>
    <div class="signedQuoteRow"><strong>Angebot:</strong> {WebUtility.HtmlEncode(model.QuoteNo)}</div>
    <div class="signedQuoteRow"><strong>Projekt:</strong> {WebUtility.HtmlEncode(model.ProjectTitle)}</div>
    <div class="signedQuoteRow"><strong>Name:</strong> {WebUtility.HtmlEncode(signerName ?? "")}</div>
    <div class="signedQuoteRow"><strong>E-Mail:</strong> {WebUtility.HtmlEncode(signerEmail ?? "")}</div>
    <div class="signedQuoteRow"><strong>Signiert am:</strong> {WebUtility.HtmlEncode(signedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))}</div>

    <div class="signedQuoteImage">
      <img src="{WebUtility.HtmlEncode(signatureDataUrl ?? "")}" alt="Signature" />
      <div class="signedQuoteLine"></div>
    </div>
  </div>
</div>
""";

            if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</head>", extraStyle + "</head>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html = extraStyle + html;
            }

            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", signatureBlock + "</body>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html += signatureBlock;
            }

            return html;
        }
    }
}

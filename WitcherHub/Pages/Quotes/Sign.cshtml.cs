using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Pdf;
using WitcherHub.Infrastructure.Services.Quotes;
using static WitcherHub.Infrastructure.Data.Models.Enums;
using WitcherHub.Application.Models.Email;

namespace WitcherHub.Pages.Quotes
{
    [AllowAnonymous]
    public class SignModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly QuotePublicLinkService _quotePublicLinkService;
        private readonly ILogger<SignModel> _logger;
        private readonly ContractCreationService _contractCreationService;
        private readonly IAppCache _cache;
        private readonly IPdfGenerator _pdf;
        private readonly IEmailService _email;
        private readonly IEmailTemplateRenderer _emailRenderer;

        public SignModel(
            AppDbContext db,
            QuotePublicLinkService quotePublicLinkService,
            ContractCreationService contractCreationService,
            ILogger<SignModel> logger,
    IAppCache cache,
    IPdfGenerator pdf,
    IEmailService email,
    IEmailTemplateRenderer emailRenderer)
        {
            _db = db;
            _quotePublicLinkService = quotePublicLinkService;
            _contractCreationService = contractCreationService;
            _logger = logger;
            _cache = cache;
            _email = email;
            _pdf = pdf;
            _emailRenderer = emailRenderer;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        [BindProperty(SupportsGet = true, Name = "t")]
        public string? Token { get; set; }

        public string QuoteHtml { get; private set; } = "";

        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public string? SignatureDataUrl { get; private set; }

        public string? SignerNamePrefill { get; private set; }
        public string? SignerEmailPrefill { get; private set; }

        public string ProviderName { get; private set; } = "";
        public string ProviderAddress { get; private set; } = "";

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return NotFound();

            if (string.IsNullOrWhiteSpace(Token))
                return Unauthorized();

            var link = await _quotePublicLinkService.ValidateActiveLinkAsync(Token, ct);
            if (link is null || link.QuoteId != Id)
                return Unauthorized();

            await _quotePublicLinkService.MarkOpenedAsync(link.Id, ct);

            var quote = await LoadQuoteAsync(ct);
            if (quote is null)
                return NotFound();

            var docModel = BuildQuotePdfModel(quote);

            ProviderName = docModel.CompanyName;
            ProviderAddress = string.Join("\n", new[]
            {
                docModel.CompanyLine1,
                docModel.CompanyLine2,
                docModel.CompanyLine3,
                docModel.CompanyEmail
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var customer = quote.Project.Customer;
            var defaultName = customer.Type == CustomerType.Individual
                ? BuildName(customer.FirstName, customer.LastName, customer.Name)
                : (customer.Name ?? "").Trim();

            SignerNamePrefill = string.IsNullOrWhiteSpace(defaultName) ? "Customer" : defaultName;
            SignerEmailPrefill = link.RecipientEmail;

            if (quote.Status == DocumentStatus.Signed || quote.SignedAt is not null)
            {
                IsSigned = true;
                SignedAtIso = (quote.SignedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o");
            }

            var sig = quote.Signatures
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            if (sig is not null)
            {
                if (sig.SignedAt is not null)
                {
                    IsSigned = true;
                    SignedAtIso = sig.SignedAt.Value.UtcDateTime.ToString("o");
                }

                if (sig.SignatureData is not null &&
                    sig.SignatureData.RootElement.TryGetProperty("dataUrl", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    SignatureDataUrl = p.GetString();
                }

                if (!string.IsNullOrWhiteSpace(sig.SignerName))
                    SignerNamePrefill = sig.SignerName;

                if (!string.IsNullOrWhiteSpace(sig.SignerEmail))
                    SignerEmailPrefill = sig.SignerEmail;
            }

            var fullHtml = QuotePdfHtmlBuilder.Build(docModel);
            QuoteHtml = ExtractRenderableHtml(fullHtml);

            return Page();
        }

        public async Task<IActionResult> OnPostSignAsync([FromQuery(Name = "t")] string? t, CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return new JsonResult(new { ok = false, message = "Invalid quote id." }) { StatusCode = 400 };

            if (string.IsNullOrWhiteSpace(t))
                return new JsonResult(new { ok = false, message = "Unauthorized." }) { StatusCode = 401 };

            Token = t.Trim();

            var link = await _quotePublicLinkService.ValidateActiveLinkAsync(Token, ct);
            if (link is null || link.QuoteId != Id)
                return new JsonResult(new { ok = false, message = "Unauthorized." }) { StatusCode = 401 };

            var signerName = (Request.Form["SignerName"].ToString() ?? "").Trim();
            var signerEmail = (Request.Form["SignerEmail"].ToString() ?? "").Trim();
            var signatureDataUrl = (Request.Form["SignatureDataUrl"].ToString() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(signerName) || string.IsNullOrWhiteSpace(signerEmail))
                return new JsonResult(new { ok = false, code = "FIELDS_REQUIRED" }) { StatusCode = 400 };

            if (!System.Text.RegularExpressions.Regex.IsMatch(signerEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                return new JsonResult(new { ok = false, code = "INVALID_EMAIL" }) { StatusCode = 400 };

            if (string.IsNullOrWhiteSpace(signatureDataUrl) || !signatureDataUrl.StartsWith("data:image/"))
                return new JsonResult(new { ok = false, message = "Invalid signature data." }) { StatusCode = 400 };

            var now = DateTimeOffset.UtcNow;

            var updated = await _db.Quotes
                .Where(q => q.Id == Id && q.SignedAt == null && q.Status != DocumentStatus.Signed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.SignedAt, now)
                    .SetProperty(q => q.Status, DocumentStatus.Signed),
                    ct);

            if (updated == 0)
            {
                var exists = await _db.Quotes.AnyAsync(q => q.Id == Id, ct);
                if (!exists)
                    return new JsonResult(new { ok = false, message = "Quote not found." }) { StatusCode = 404 };

                return new JsonResult(new { ok = false, message = "Quote already signed." }) { StatusCode = 409 };
            }

            var payload = JsonSerializer.SerializeToDocument(new
            {
                dataUrl = signatureDataUrl,
                userAgent = Request.Headers.UserAgent.ToString(),
                signedAt = now.UtcDateTime.ToString("o")
            });

            _db.QuoteSignatures.Add(new QuoteSignature
            {
                QuoteId = Id,
                SignerName = signerName,
                SignerEmail = signerEmail,
                SignedAt = now,
                SignatureData = payload
            });

            await _db.SaveChangesAsync(ct);

            await _cache.RemoveAsync(QuoteCacheKeys.Details(Id), ct);
            await _cache.BumpVersionAsync(QuoteCacheKeys.ListVersionKey, ct);

            var emailQueued = false;

            try
            {
                var signedQuote = await LoadQuoteAsync(ct);
                if (signedQuote is not null)
                {
                    await SendSignedQuoteEmailAsync(
                        signedQuote,
                        signerName,
                        signerEmail,
                        now,
                        signatureDataUrl,
                        ct);

                    emailQueued = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quote signed but signed confirmation email failed. QuoteId={QuoteId}", Id);
            }

            _logger.LogInformation("Quote signed successfully. QuoteId={QuoteId}", Id);

            return new JsonResult(new
            {
                ok = true,
                signedAtIso = now.UtcDateTime.ToString("o"),
                emailQueued
            });
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
    margin: 28px 0 0 0;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .signedQuoteCard{
    border: 1px solid #dbe3ee;
    border-radius: 14px;
    padding: 18px 20px;
    background: #ffffff;
  }

  .signedQuoteTitle{
    font-size: 18px;
    font-weight: 800;
    color: #0f172a;
    margin: 0 0 14px 0;
  }

  .signedQuoteRow{
    margin: 6px 0;
    font-size: 12.5px;
    line-height: 1.6;
    color: #111827;
  }

  .signedQuoteRow strong{
    display: inline-block;
    min-width: 120px;
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
    border-top: 1px solid #111827;
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


        private async Task SendSignedQuoteEmailAsync(
    Quote quote,
    string signerName,
    string signerEmail,
    DateTimeOffset signedAt,
    string signatureDataUrl,
    CancellationToken ct)
        {
            var model = BuildQuotePdfModel(quote);

            var html = BuildSignedQuotePdfHtml(
                model,
                signerName,
                signerEmail,
                signedAt,
                signatureDataUrl);

            var pdfBytes = _pdf.FromHtml(html, $"Angebot {model.QuoteNo} - signed");

            var subject = $"Ihr unterschriebenes Angebot {model.QuoteNo}";

            var emailHtml = await _emailRenderer.RenderAsync(
                "QuoteSignedConfirmation",
                new
                {
                    Subject = subject,
                    UserName = string.IsNullOrWhiteSpace(signerName)
                        ? (model.Customer.DisplayName ?? "Kunde")
                        : signerName,
                    QuoteNo = model.QuoteNo,
                    ProjectTitle = model.ProjectTitle,
                    SignedAt = signedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                },
                ct);

            var msg = new EmailMessage
            {
                From = new EmailAddress("no-reply@invalid.local", "WitcherHub"),
                Subject = subject,
                HtmlBody = emailHtml,
                TextBody = null,
                To = [],
                Bcc = [new EmailAddress(signerEmail, signerName)],
                Attachments =
                [
                    new EmailAttachment($"{model.QuoteNo}-signed.pdf", "application/pdf", pdfBytes)
                ]
            };

            await _email.QueueNowAsync(msg, ct);
        }
        private async Task<Quote?> LoadQuoteAsync(CancellationToken ct)
        {
            return await _db.Quotes
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(c => c.Addresses)
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(c => c.EmailAddresses)
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(c => c.Contacts)
                .Include(q => q.Items)
                    .ThenInclude(i => i.Service)
                .Include(q => q.Signatures)
                .FirstOrDefaultAsync(q => q.Id == Id, ct);
        }
        private static GenerateContractDocumentRequest BuildGenerateContractRequestFromQuote(
    Quote quote,
    string signerName,
    string signerEmail)
        {
            return new GenerateContractDocumentRequest
            {
                ProjectId = quote.ProjectId,
                ContractNo = null,
                ProjectTitle = string.IsNullOrWhiteSpace(quote.Project?.Title) ? "Project" : quote.Project.Title!,
                Currency = string.IsNullOrWhiteSpace(quote.Currency) ? "EUR" : quote.Currency!,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = null,

                SignerName = signerName,
                SignerEmail = signerEmail,

                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,

                Services = (quote.Items ?? new List<QuoteItem>())
                    .OrderBy(x => x.Position)
                    .Select((x, index) => new ContractServiceLineDto
                    {
                        Position = x.Position > 0 ? x.Position : index + 1,
                        Title = string.IsNullOrWhiteSpace(x.Title) ? $"Position {index + 1}" : x.Title.Trim(),
                        ServiceName = x.Service?.Name,
                        ServiceType = x.Service?.ServiceType.ToString(),
                        PricingModel = x.Service?.PricingModel.ToString(),
                        AgreedPrice = ResolveQuoteItemAgreedPrice(x),
                        Config = JsonDocumentToDictionary(x.Config)
                    })
                    .ToList()
            };
        }

        private static decimal? ResolveQuoteItemAgreedPrice(QuoteItem item)
        {
            var baseTotal = item.Quantity * item.UnitPrice;

            var total = ReadDec(item.PriceBreakdown, "total", 0m);
            if (total > 0m)
                return total;

            var subTotal = ReadDec(item.PriceBreakdown, "subTotal", baseTotal);
            return subTotal > 0m ? subTotal : baseTotal;
        }

        private static Dictionary<string, object> JsonDocumentToDictionary(JsonDocument? doc)
        {
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in doc.RootElement.EnumerateObject())
                result[p.Name] = JsonElementToObject(p.Value) ?? string.Empty;

            return result;
        }

        private static object? JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(
                        x => x.Name,
                        x => JsonElementToObject(x.Value) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase),

                JsonValueKind.Array => element.EnumerateArray()
                    .Select(JsonElementToObject)
                    .ToList(),

                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetDecimal(out var m)
                    ? m
                    : element.TryGetDouble(out var d)
                        ? d
                        : element.GetRawText(),

                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        private static QuotePdfHtmlBuilder.QuotePdfDocumentModel BuildQuotePdfModel(Quote q)
        {
            var customer = q.Project.Customer;

            var addr = customer.Addresses?
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            var email =
                customer.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => c.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?
                    .Where(ea => ea.Kind == "business")
                    .Select(ea => ea.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?
                    .Select(ea => ea.Email)
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            var isIndividual = customer.Type == CustomerType.Individual;

            var personName = BuildName(customer.FirstName, customer.LastName, customer.Name);
            var companyName = (customer.Name ?? "").Trim();

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

            var lines = new List<QuotePdfHtmlBuilder.QuotePdfLine>();
            decimal sumSub = 0m;
            decimal sumDisc = 0m;

            foreach (var it in (q.Items ?? []).OrderBy(x => x.Position))
            {
                var baseTotal = it.Quantity * it.UnitPrice;

                var sub = ReadDec(it.PriceBreakdown, "subTotal", baseTotal);
                var disc = ReadNestedDec(it.PriceBreakdown, "discount", "amount", 0m);
                var total = ReadDec(it.PriceBreakdown, "total", sub);

                lines.Add(new QuotePdfHtmlBuilder.QuotePdfLine
                {
                    Position = it.Position,
                    Title = it.Title ?? "",
                    ServiceName = it.Service?.Name,
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice,
                    SubTotal = sub,
                    Discount = disc,
                    Tax = 0m,
                    Total = total
                });

                sumSub += sub;
                sumDisc += disc;
            }

            var vatPercent = q.ApplyVat ? 19m : 0m;
            var sumTax = q.ApplyVat ? Math.Max(0m, sumSub * 0.19m) : 0m;
            var sumTotal = sumSub + sumTax;

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
                ProjectTitle = q.Project?.Title ?? "",

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

        private static string ExtractRenderableHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var styleBlock = ExtractStyleBlock(html);
            var bodyBlock = ExtractBodyBlock(html);

            var scopedStyleBlock = ScopeQuoteStyleBlock(styleBlock, ".quotePdfScope");

            return (scopedStyleBlock ?? "") + bodyBlock;
        }
        private static string ScopeQuoteStyleBlock(string htmlStyleBlock, string scopeSelector)
        {
            if (string.IsNullOrWhiteSpace(htmlStyleBlock))
                return string.Empty;

            var css = htmlStyleBlock;

            css = Regex.Replace(
                css,
                @"@page\s*\{.*?\}",
                "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            css = css.Replace("html, body", scopeSelector, StringComparison.OrdinalIgnoreCase);
            css = css.Replace("body, html", scopeSelector, StringComparison.OrdinalIgnoreCase);

            css = Regex.Replace(
                css,
                @"(?<![-\w])body(?![-\w])",
                scopeSelector,
                RegexOptions.IgnoreCase);

            css = Regex.Replace(
                css,
                @"(?<![-\w])html(?![-\w])",
                scopeSelector,
                RegexOptions.IgnoreCase);

            return css;
        }
        private static string ExtractStyleBlock(string html)
        {
            var start = html.IndexOf("<style", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            var openEnd = html.IndexOf('>', start);
            if (openEnd < 0)
                return string.Empty;

            var close = html.IndexOf("</style>", openEnd, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                return string.Empty;

            return html.Substring(start, (close + "</style>".Length) - start);
        }

        private static string ExtractBodyBlock(string html)
        {
            var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStart < 0)
                return html;

            bodyStart = html.IndexOf('>', bodyStart);
            if (bodyStart < 0)
                return html;

            bodyStart++;

            var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyEnd < 0 || bodyEnd <= bodyStart)
                return html.Substring(bodyStart);

            return html.Substring(bodyStart, bodyEnd - bodyStart);
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
                if (v.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                    return ds;

                return fallback;
            }
            catch
            {
                return fallback;
            }
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
                if (v.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                    return ds;

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
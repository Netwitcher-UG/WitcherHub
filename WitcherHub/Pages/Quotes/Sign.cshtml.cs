using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Infrastructure.Services.Pdf;
using WitcherHub.Infrastructure.Services.Quotes;
using static WitcherHub.Infrastructure.Data.Models.Enums;

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
        private readonly IBackgroundTaskQueue _bg;
        private readonly IServiceScopeFactory _scopeFactory;

        public SignModel(
            AppDbContext db,
            QuotePublicLinkService quotePublicLinkService,
            ContractCreationService contractCreationService,
            ILogger<SignModel> logger,
            IAppCache cache,
            IPdfGenerator pdf,
            IEmailService email,
            IEmailTemplateRenderer emailRenderer,
            IBackgroundTaskQueue bg,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _quotePublicLinkService = quotePublicLinkService;
            _contractCreationService = contractCreationService;
            _logger = logger;
            _cache = cache;
            _pdf = pdf;
            _email = email;
            _emailRenderer = emailRenderer;
            _bg = bg;
            _scopeFactory = scopeFactory;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        [BindProperty(SupportsGet = true, Name = "t")]
        public string? Token { get; set; }

        public string QuoteHtml { get; private set; } = string.Empty;
        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public string? SignatureDataUrl { get; private set; }
        public string? SignerNamePrefill { get; private set; }
        public string? SignerEmailPrefill { get; private set; }
        public string ProviderName { get; private set; } = string.Empty;
        public string ProviderAddress { get; private set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(Token))
            {
                return Unauthorized();
            }

            var link = await _quotePublicLinkService.ValidateActiveLinkAsync(Token, ct);
            if (link is null || link.QuoteId != Id)
            {
                return Unauthorized();
            }

            await _quotePublicLinkService.MarkOpenedAsync(link.Id, ct);

            var quote = await LoadQuoteAsync(ct);
            if (quote is null)
            {
                return NotFound();
            }

            var docModel = BuildQuotePdfModel(quote);

            ProviderName = docModel.CompanyName;
            ProviderAddress = string.Join(
                "\n",
                new[]
                {
                    docModel.CompanyLine1,
                    docModel.CompanyLine2,
                    docModel.CompanyLine3,
                    docModel.CompanyEmail
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var customer = quote.Project.Customer;
            var defaultName = customer.Type == CustomerType.Individual
                ? BuildName(customer.FirstName, customer.LastName, customer.Name)
                : (customer.Name ?? string.Empty).Trim();

            SignerNamePrefill = string.IsNullOrWhiteSpace(defaultName)
                ? "Customer"
                : defaultName;

            SignerEmailPrefill = link.RecipientEmail;

            if (quote.Status == DocumentStatus.Signed || quote.SignedAt is not null)
            {
                IsSigned = true;
                SignedAtIso = (quote.SignedAt ?? DateTimeOffset.UtcNow)
                    .UtcDateTime
                    .ToString("o");
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
                    sig.SignatureData.RootElement.TryGetProperty("dataUrl", out var dataUrlProp) &&
                    dataUrlProp.ValueKind == JsonValueKind.String)
                {
                    SignatureDataUrl = dataUrlProp.GetString();
                }

                if (!string.IsNullOrWhiteSpace(sig.SignerName))
                {
                    SignerNamePrefill = sig.SignerName;
                }

                if (!string.IsNullOrWhiteSpace(sig.SignerEmail))
                {
                    SignerEmailPrefill = sig.SignerEmail;
                }
            }

            var fullHtml = QuotePdfHtmlBuilder.Build(docModel);
            QuoteHtml = ExtractRenderableHtml(fullHtml);

            return Page();
        }

        public async Task<IActionResult> OnPostSignAsync(
            [FromQuery(Name = "t")] string? t,
            CancellationToken ct)
        {
            if (Id == Guid.Empty)
            {
                return new JsonResult(new { ok = false, message = "Invalid quote id." })
                {
                    StatusCode = 400
                };
            }

            if (string.IsNullOrWhiteSpace(t))
            {
                return new JsonResult(new { ok = false, message = "Unauthorized." })
                {
                    StatusCode = 401
                };
            }

            Token = t.Trim();

            var link = await _quotePublicLinkService.ValidateActiveLinkAsync(Token, ct);
            if (link is null || link.QuoteId != Id)
            {
                return new JsonResult(new { ok = false, message = "Unauthorized." })
                {
                    StatusCode = 401
                };
            }

            var signerName = (Request.Form["SignerName"].ToString() ?? string.Empty).Trim();
            var signerEmail = (Request.Form["SignerEmail"].ToString() ?? string.Empty).Trim();
            var signatureDataUrl = (Request.Form["SignatureDataUrl"].ToString() ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(signerName) || string.IsNullOrWhiteSpace(signerEmail))
            {
                return new JsonResult(new { ok = false, code = "FIELDS_REQUIRED" })
                {
                    StatusCode = 400
                };
            }

            if (!Regex.IsMatch(signerEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                return new JsonResult(new { ok = false, code = "INVALID_EMAIL" })
                {
                    StatusCode = 400
                };
            }

            if (string.IsNullOrWhiteSpace(signatureDataUrl) ||
                !signatureDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { ok = false, message = "Invalid signature data." })
                {
                    StatusCode = 400
                };
            }

            var now = DateTimeOffset.UtcNow;

            var updated = await _db.Quotes
                .Where(q => q.Id == Id && q.SignedAt == null && q.Status != DocumentStatus.Signed)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(q => q.SignedAt, now)
                        .SetProperty(q => q.Status, DocumentStatus.Signed),
                    ct);

            if (updated == 0)
            {
                var exists = await _db.Quotes.AnyAsync(q => q.Id == Id, ct);

                if (!exists)
                {
                    return new JsonResult(new { ok = false, message = "Quote not found." })
                    {
                        StatusCode = 404
                    };
                }

                return new JsonResult(new { ok = false, message = "Quote already signed." })
                {
                    StatusCode = 409
                };
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
            var afterSignQueued = false;
            string? afterSignAction = null;
            Quote? signedQuote = null;

            try
            {
                signedQuote = await LoadQuoteAsync(ct);

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
                _logger.LogError(
                    ex,
                    "Quote signed but signed confirmation email failed. QuoteId={QuoteId}",
                    Id);
            }

            if (signedQuote is not null)
            {
                try
                {
                    if (signedQuote.AfterCustomerSignAction == QuoteAfterSignAction.Contract)
                    {
                        afterSignAction = "contract";

                        await QueueCreateContractFromQuoteAsync(
                            signedQuote.Id,
                            signerName,
                            signerEmail);

                        afterSignQueued = true;
                    }
                    else if (signedQuote.AfterCustomerSignAction == QuoteAfterSignAction.Invoice)
                    {
                        afterSignAction = "invoice";
                        await QueueInvoiceFlowFromQuoteAsync(signedQuote.Id);
                        afterSignQueued = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Quote after-sign action queue failed. QuoteId={QuoteId}, Action={Action}",
                        signedQuote.Id,
                        signedQuote.AfterCustomerSignAction);
                }
            }

            _logger.LogInformation(
                "Quote signed successfully. QuoteId={QuoteId}, AfterSignAction={AfterSignAction}, AfterSignQueued={AfterSignQueued}",
                Id,
                afterSignAction,
                afterSignQueued);

            return new JsonResult(new
            {
                ok = true,
                signedAtIso = now.UtcDateTime.ToString("o"),
                emailQueued,
                afterSignAction,
                afterSignQueued
            });
        }

        private async Task QueueCreateContractFromQuoteAsync(
            Guid quoteId,
            string signerName,
            string signerEmail)
        {
            await _bg.QueueAsync(async token =>
            {
                using var scope = _scopeFactory.CreateScope();

                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var contractCreationService = scope.ServiceProvider.GetRequiredService<ContractCreationService>();

                    var quote = await db.Quotes
                        .Include(q => q.Project)
                        .Include(q => q.Items)
                            .ThenInclude(i => i.Service)
                        .FirstOrDefaultAsync(q => q.Id == quoteId, token);

                    if (quote is null)
                    {
                        _logger.LogWarning(
                            "Create contract from quote skipped: quote not found. QuoteId={QuoteId}",
                            quoteId);

                        return;
                    }

                    var request = BuildGenerateContractRequestFromQuote(
                        quote,
                        signerName,
                        signerEmail);

                    var contractId = await contractCreationService.GenerateAndCreateAsync(request, token);

                    _logger.LogInformation(
                        "Contract created from signed quote. QuoteId={QuoteId}, ContractId={ContractId}",
                        quoteId,
                        contractId);
                }
                catch (OperationCanceledException oce)
                {
                    _logger.LogWarning(
                        oce,
                        "Create contract from quote canceled. QuoteId={QuoteId}",
                        quoteId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Create contract from quote failed. QuoteId={QuoteId}",
                        quoteId);

                    throw;
                }
            });
        }

        private async Task QueueInvoiceFlowFromQuoteAsync(Guid quoteId)
        {
            await _bg.QueueAsync(async token =>
            {
                using var scope = _scopeFactory.CreateScope();

                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var lex = scope.ServiceProvider.GetRequiredService<LexwareInvoiceSyncService>();

                    var quote = await db.Quotes
                        .Include(q => q.Items)
                        .FirstOrDefaultAsync(q => q.Id == quoteId, token);

                    if (quote is null)
                    {
                        _logger.LogWarning(
                            "Quote not found in background invoice job. QuoteId={QuoteId}",
                            quoteId);

                        return;
                    }

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var hasRecurringItems = quote.Items.Any(i => i.BillingCycle != BillingCycle.OneTime);
                    var hasOneTimeItems = quote.Items.Any(i => i.BillingCycle == BillingCycle.OneTime);

                    if (hasRecurringItems)
                    {
                        var start = quote.RecurringStartDate
                            ?? (quote.SignedAt.HasValue
                                ? DateOnly.FromDateTime(quote.SignedAt.Value.UtcDateTime)
                                : quote.IssuedAt.HasValue
                                    ? DateOnly.FromDateTime(quote.IssuedAt.Value.UtcDateTime)
                                    : today);

                        quote.RecurringEnabled = true;
                        quote.RecurringIsActive = true;

                        if (!quote.RecurringStartDate.HasValue)
                        {
                            quote.RecurringStartDate = start;
                        }

                        if (!quote.NextRecurringInvoiceDate.HasValue)
                        {
                            quote.NextRecurringInvoiceDate = start;
                        }

                        await db.SaveChangesAsync(token);
                    }

                    if (quote.InvoiceSendMode == InvoiceSendMode.Automatic)
                    {
                        if (hasOneTimeItems)
                        {
                            await lex.CreateOneTimeInvoiceFromQuoteAsync(quoteId, token);
                        }

                        if (hasRecurringItems &&
                            quote.NextRecurringInvoiceDate.HasValue &&
                            quote.NextRecurringInvoiceDate.Value <= today)
                        {
                            await lex.CreateRecurringInvoiceFromQuoteAsync(
                                quoteId,
                                quote.NextRecurringInvoiceDate.Value,
                                token);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Invoice generation skipped because quote uses manual mode. QuoteId={QuoteId}",
                            quoteId);
                    }

                    _logger.LogInformation(
                        "Quote invoice background flow completed. QuoteId={QuoteId}",
                        quoteId);
                }
                catch (OperationCanceledException oce)
                {
                    _logger.LogWarning(
                        oce,
                        "Quote invoice background flow canceled. QuoteId={QuoteId}",
                        quoteId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Quote invoice background flow failed. QuoteId={QuoteId}",
                        quoteId);

                    throw;
                }
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

        private async Task SendSignedQuoteEmailAsync(
            Quote quote,
            string signerName,
            string signerEmail,
            DateTimeOffset signedAt,
            string signatureDataUrl,
            CancellationToken ct)
        {
            var model = BuildQuotePdfModel(quote);
            var html = BuildSignedQuotePdfHtml(model, signerName, signerEmail, signedAt, signatureDataUrl);
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
                To = new List<EmailAddress>(),
                Bcc = new List<EmailAddress>
                {
                    new EmailAddress(signerEmail, signerName)
                },
                Attachments = new List<EmailAttachment>
                {
                    new EmailAttachment(
                        $"{model.QuoteNo}-signed.pdf",
                        "application/pdf",
                        pdfBytes)
                }
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
                ProjectTitle = string.IsNullOrWhiteSpace(quote.Project?.Title)
                    ? "Project"
                    : quote.Project.Title!,
                Currency = string.IsNullOrWhiteSpace(quote.Currency)
                    ? "EUR"
                    : quote.Currency!,
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
                        Title = string.IsNullOrWhiteSpace(x.Title)
                            ? $"Position {index + 1}"
                            : x.Title.Trim(),
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
            {
                return total;
            }

            var subTotal = ReadDec(item.PriceBreakdown, "subTotal", baseTotal);
            return subTotal > 0m ? subTotal : baseTotal;
        }

        private static Dictionary<string, object> JsonDocumentToDictionary(JsonDocument? doc)
        {
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                result[property.Name] = JsonElementToObject(property.Value) ?? string.Empty;
            }

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
            var companyName = (customer.Name ?? string.Empty).Trim();

            string? pdfCompanyName = null;
            var pdfDisplayName = string.Empty;

            if (isIndividual)
            {
                pdfCompanyName = null;
                pdfDisplayName = personName;
            }
            else
            {
                pdfCompanyName = companyName;
                pdfDisplayName = string.Empty;
            }

            var vatPercent = q.ApplyVat ? 19m : 0m;

            var lines = new List<QuotePdfHtmlBuilder.QuotePdfLine>();

            decimal sumBase = 0m; // قبل الخصم
            decimal sumDisc = 0m; // الخصم
            decimal sumNet = 0m;  // بعد الخصم

            foreach (var it in (q.Items ?? new List<QuoteItem>()).OrderBy(x => x.Position))
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
                    Title = it.Title ?? string.Empty,
                    Description = string.IsNullOrWhiteSpace(it.Description) ? null : it.Description.Trim(),
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice,
                    BillingCycleText = MapBillingCycleText(it.BillingCycle),
                    VatPercent = vatPercent,
                    DiscountDisplay = BuildDiscountDisplay(it.DiscountType, it.DiscountValue),
                    SubTotal = baseTotal, // صار قبل الخصم
                    Discount = disc,
                    Tax = 0m,
                    Total = total         // الصافي بعد الخصم
                });

                sumBase += baseTotal;
                sumDisc += disc;
                sumNet += total;
            }

            var sumTax = q.ApplyVat ? Math.Max(0m, sumNet * 0.19m) : 0m;
            var sumTotal = sumNet + sumTax;

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
                ProjectTitle = q.Project?.Title ?? string.Empty,
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
        private static string ExtractRenderableHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var styleBlock = ExtractStyleBlock(html);
            var bodyBlock = ExtractBodyBlock(html);
            var scopedStyleBlock = ScopeQuoteStyleBlock(styleBlock, ".quotePdfScope");

            return scopedStyleBlock + bodyBlock;
        }

        private static string ScopeQuoteStyleBlock(string htmlStyleBlock, string scopeSelector)
        {
            if (string.IsNullOrWhiteSpace(htmlStyleBlock))
            {
                return string.Empty;
            }

            var css = htmlStyleBlock;

            css = Regex.Replace(
                css,
                @"@page\s*\{.*?\}",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            css = css.Replace("html, body", scopeSelector, StringComparison.OrdinalIgnoreCase);
            css = css.Replace("body, html", scopeSelector, StringComparison.OrdinalIgnoreCase);
            css = Regex.Replace(css, @"(?<![-\w])body(?![-\w])", scopeSelector, RegexOptions.IgnoreCase);
            css = Regex.Replace(css, @"(?<![-\w])html(?![-\w])", scopeSelector, RegexOptions.IgnoreCase);

            return css;
        }

        private static string ExtractStyleBlock(string html)
        {
            var start = html.IndexOf("<style", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            var openEnd = html.IndexOf('>', start);
            if (openEnd < 0)
            {
                return string.Empty;
            }

            var close = html.IndexOf("</style>", openEnd, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                return string.Empty;
            }

            return html.Substring(start, (close + "</style>".Length) - start);
        }

        private static string ExtractBodyBlock(string html)
        {
            var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStart < 0)
            {
                return html;
            }

            bodyStart = html.IndexOf('>', bodyStart);
            if (bodyStart < 0)
            {
                return html;
            }

            bodyStart++;

            var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyEnd < 0 || bodyEnd <= bodyStart)
            {
                return html.Substring(bodyStart);
            }

            return html.Substring(bodyStart, bodyEnd - bodyStart);
        }

        private static string BuildName(string? first, string? last, string? fallback)
        {
            var f = (first ?? string.Empty).Trim();
            var l = (last ?? string.Empty).Trim();
            var both = (f + " " + l).Trim();

            return string.IsNullOrWhiteSpace(both)
                ? (fallback ?? string.Empty).Trim()
                : both;
        }

        private static decimal ReadDec(JsonDocument? doc, string prop, decimal fallback = 0m)
        {
            try
            {
                if (doc is null)
                {
                    return fallback;
                }

                var root = doc.RootElement;
                if (!root.TryGetProperty(prop, out var value))
                {
                    return fallback;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
                {
                    return decimalValue;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(
                        value.GetString(),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static decimal ReadNestedDec(
            JsonDocument? doc,
            string parent,
            string prop,
            decimal fallback = 0m)
        {
            try
            {
                if (doc is null)
                {
                    return fallback;
                }

                var root = doc.RootElement;
                if (!root.TryGetProperty(parent, out var parentElement))
                {
                    return fallback;
                }

                if (parentElement.ValueKind != JsonValueKind.Object)
                {
                    return fallback;
                }

                if (!parentElement.TryGetProperty(prop, out var value))
                {
                    return fallback;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
                {
                    return decimalValue;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(
                        value.GetString(),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}

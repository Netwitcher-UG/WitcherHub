using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Infrastructure.Services.Pdf;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    [AllowAnonymous]
    public class SignModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IContractDocumentGenerator _generator;
        private readonly ContractTemplateOptions _opt;
        private readonly IEmailTemplateRenderer _templates;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<SignModel> _logger;
        private readonly IPdfGenerator _pdf;
        private readonly IBackgroundTaskQueue _bg;
        private readonly IServiceScopeFactory _scopeFactory;

        public SignModel(
    AppDbContext db,
    IContractDocumentGenerator generator,
    IOptions<ContractTemplateOptions> opt,
    IEmailTemplateRenderer templates,
    IEmailSender emailSender,
    ILogger<SignModel> logger,
    IPdfGenerator pdf, IBackgroundTaskQueue bg, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _generator = generator;
            _opt = opt.Value;
            _templates = templates;
            _emailSender = emailSender;
            _logger = logger;
            _pdf = pdf;
            _bg = bg;
            _scopeFactory = scopeFactory;
        }


        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        [BindProperty(SupportsGet = true, Name = "t")]
        public string? Token { get; set; }

        public string ContractHtml { get; private set; } = "";
        public string ProviderName { get; private set; } = "";
        public string ProviderAddress { get; private set; } = "";

        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public string? SignatureDataUrl { get; private set; }

        public string? SignerNamePrefill { get; private set; }
        public string? SignerEmailPrefill { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            // ✅ MUST have id + token
            if (Id == Guid.Empty) return NotFound();
            if (string.IsNullOrWhiteSpace(Token)) return Unauthorized();

            var tokenHash = ContractAccessLink.HashToken(Token.Trim());

            var link = await _db.ContractAccessLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ContractId == Id &&
                    x.TokenHash == tokenHash &&
                    x.RevokedAtUtc == null &&
                    x.ExpiresAt > DateTimeOffset.UtcNow, ct);

            if (link is null) return Unauthorized();

            // update last opened (best-effort)
            try
            {
                await _db.ContractAccessLinks
                    .Where(x => x.Id == link.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastOpenedAtUtc, DateTimeOffset.UtcNow), ct);
            }
            catch
            {
                // ignore (best-effort)
            }

            var contract = await _db.Contracts
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Addresses)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Contacts)
                .Include(c => c.Items)
                    .ThenInclude(i => i.Service)
                .Include(c => c.Signatures)
                .FirstOrDefaultAsync(c => c.Id == Id, ct);

            if (contract is null) return NotFound();

            // Provider block split
            var pb = NormalizeNewLines(_opt.ProviderBlock ?? "");
            var linesPb = pb.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            ProviderName = linesPb.Length > 0 ? linesPb[0] : "";
            ProviderAddress = linesPb.Length > 1 ? string.Join("\n", linesPb.Skip(1)) : "";

            // Prefill from customer (safe)
            var customer = contract.Project.Customer;
            SignerNamePrefill = customer.Name;

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

            // ✅ enforce recipient email from link (strong)
            SignerEmailPrefill = link.RecipientEmail;

            // Generate Terms once
            if (string.IsNullOrWhiteSpace(contract.Terms))
            {
                if (contract.Items == null || contract.Items.Count == 0)
                    return BadRequest("Contract has no Positions.");

                var req = BuildRequestFromDb(contract);
                var doc = await _generator.GenerateAsync(req, ct);
                contract.Terms = NormalizeNewLines(doc.FullDocument);

                await _db.SaveChangesAsync(ct);
            }

            // Signed state
            if (contract.Status == DocumentStatus.Signed || contract.SignedAt is not null)
            {
                IsSigned = true;
                SignedAtIso = (contract.SignedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o");
            }

            var sig = contract.Signatures.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
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

                if (!string.IsNullOrWhiteSpace(sig.SignerName)) SignerNamePrefill = sig.SignerName;
                if (!string.IsNullOrWhiteSpace(sig.SignerEmail)) SignerEmailPrefill = sig.SignerEmail;
            }

            // Markdown -> HTML (sanitized)
            var model = BuildContractPdfModel(
    contract,
    showSignaturePlaceholder: false,
    notesText: "");

            var fullHtml = ContractPdfHtmlBuilder.Build(model);

            var logoPath = Url.Content("~/theme/assets/images/netwitcher-logo.png")
                          ?? "/theme/assets/images/netwitcher-logo.png";

            var logoUrl = $"{Request.Scheme}://{Request.Host}{logoPath}";

            fullHtml = fullHtml.Replace("__NETWITCHER_LOGO__", logoUrl, StringComparison.OrdinalIgnoreCase);

            ContractHtml = ExtractRenderableHtml(fullHtml);

            return Page();
        }

        // SignModel.cs
        // استبدل الدالة بالكامل: OnPostSignAsync

        public async Task<IActionResult> OnPostSignAsync([FromQuery(Name = "t")] string? t, CancellationToken ct)
        {
            _logger.LogInformation("OnPostSignAsync HIT. Id={Id}, QueryTokenPresent={HasToken}, FormName={SignerName}, FormEmail={SignerEmail}",
        Id,
        !string.IsNullOrWhiteSpace(t),
        Request.Form["SignerName"].ToString(),
        Request.Form["SignerEmail"].ToString());
            if (Id == Guid.Empty)
                return new JsonResult(new { ok = false, message = "Invalid contract id." }) { StatusCode = 400 };

            if (string.IsNullOrWhiteSpace(t))
                return new JsonResult(new { ok = false, message = "Unauthorized." }) { StatusCode = 401 };

            Token = t.Trim();
            var tokenHash = ContractAccessLink.HashToken(Token);

            var link = await _db.ContractAccessLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ContractId == Id &&
                    x.TokenHash == tokenHash &&
                    x.RevokedAtUtc == null &&
                    x.ExpiresAt > DateTimeOffset.UtcNow, ct);

            if (link is null)
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

            var updated = await _db.Contracts
                .Where(c => c.Id == Id && c.SignedAt == null && c.Status != DocumentStatus.Signed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.SignedAt, now)
                    .SetProperty(c => c.Status, DocumentStatus.Signed),
                    ct);

            if (updated == 0)
            {
                var exists = await _db.Contracts.AnyAsync(c => c.Id == Id, ct);
                if (!exists) return new JsonResult(new { ok = false, message = "Contract not found." }) { StatusCode = 404 };
                return new JsonResult(new { ok = false, message = "Contract already signed." }) { StatusCode = 409 };
            }

            var payload = JsonSerializer.SerializeToDocument(new
            {
                dataUrl = signatureDataUrl,
                userAgent = Request.Headers.UserAgent.ToString(),
                signedAt = now.UtcDateTime.ToString("o")
            });

            _db.ContractSignatures.Add(new ContractSignature
            {
                ContractId = Id,
                SignerName = signerName,
                SignerEmail = signerEmail,
                SignedAt = now,
                SignatureData = payload
            });

            await _db.SaveChangesAsync(ct);

            // ========= BACKGROUND: Lexware =========
            var contractId = Id;
            _logger.LogInformation("Queued lexware job for ContractId={Id}", contractId);

            await _bg.QueueAsync(async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    _logger.LogInformation("Lexware job START ContractId={Id}", contractId);

                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var lex = scope.ServiceProvider.GetRequiredService<LexwareInvoiceSyncService>();

                    var contract = await db.Contracts
                        .Include(c => c.Items)
                        .FirstOrDefaultAsync(c => c.Id == contractId, token);

                    if (contract == null)
                    {
                        _logger.LogWarning("Contract not found in background job. ContractId={Id}", contractId);
                        return;
                    }

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);

                    var hasRecurringItems = contract.Items.Any(i => i.BillingCycle != BillingCycle.OneTime);
                    var hasOneTimeItems = contract.Items.Any(i => i.BillingCycle == BillingCycle.OneTime);

                    if (hasRecurringItems)
                    {
                        var start = contract.RecurringStartDate ?? contract.StartDate ?? today;

                        contract.RecurringEnabled = true;
                        contract.RecurringIsActive = true;

                        if (contract.NextRecurringInvoiceDate == null)
                            contract.NextRecurringInvoiceDate = start;

                        await db.SaveChangesAsync(token);
                    }

                    if (contract.InvoiceSendMode == InvoiceSendMode.Automatic)
                    {
                        if (hasOneTimeItems)
                        {
                            await lex.CreateOneTimeInvoiceFromContractAsync(contractId, token);
                        }

                        if (hasRecurringItems)
                        {
                            if (contract.NextRecurringInvoiceDate.HasValue && contract.NextRecurringInvoiceDate.Value <= today)
                            {
                                await lex.CreateRecurringInvoiceFromContractAsync(
                                    contractId,
                                    contract.NextRecurringInvoiceDate.Value,
                                    token);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Invoice generation skipped because contract uses manual mode. ContractId={Id}",
                            contractId);
                    }

                    _logger.LogInformation("Lexware job DONE ContractId={Id}", contractId);
                }
                catch (OperationCanceledException oce)
                {
                    _logger.LogWarning(oce, "Lexware job CANCELED ContractId={Id}", contractId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lexware job FAILED ContractId={Id}", contractId);
                    throw;
                }
            });
            // ========= BACKGROUND: Email + PDF =========
            var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : "";
            var baseUrl = $"{Request.Scheme}://{Request.Host.ToUriComponent()}{pathBase}";
            var rawToken = Token; // same token from query
            var recipientEmail = link.RecipientEmail;

            await _bg.QueueAsync(async token =>
            {
                using var scope = _scopeFactory.CreateScope();

                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var templates = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
                    var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    var pdf = scope.ServiceProvider.GetRequiredService<IPdfGenerator>();

                    var info = await db.Contracts
                        .AsNoTracking()
                        .Include(c => c.Project)
                            .ThenInclude(p => p.Customer)
                                .ThenInclude(cu => cu.Addresses)
                        .Include(c => c.Project)
                            .ThenInclude(p => p.Customer)
                                .ThenInclude(cu => cu.EmailAddresses)
                        .Include(c => c.Project)
                            .ThenInclude(p => p.Customer)
                                .ThenInclude(cu => cu.Contacts)
                        .Include(c => c.Items)
                            .ThenInclude(i => i.Service)
                        .FirstOrDefaultAsync(c => c.Id == contractId, token);

                    if (info is null)
                    {
                        _logger.LogWarning("ContractSigned email skipped: Contract not found. ContractId={ContractId}", contractId);
                        return;
                    }

                    var contractNo = info.ContractNo ?? contractId.ToString();
                    var projectTitle = info.Project?.Title ?? "Project";

                    var actionUrl = $"{baseUrl}/contracts/sign/{contractId}?t={Uri.EscapeDataString(rawToken)}";
                    var subject = $"Vertrag {contractNo} – erfolgreich unterschrieben";

                    var html = await templates.RenderAsync("ContractSigned.de", new
                    {
                        Subject = subject,
                        UserName = signerName,
                        ContractNo = contractNo,
                        ProjectTitle = projectTitle,
                        SignedAt = now.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                        ActionUrl = actionUrl
                    }, token);

                    byte[]? pdfBytes = null;
                    string? pdfError = null;

                    try
                    {
                        var pdfModel = BuildContractPdfModel(
                            info,
                            showSignaturePlaceholder: false,
                            notesText: "");

                        var logoUrl = $"{baseUrl}/theme/assets/images/netwitcher-logo.png";

                        var pdfHtml = BuildSignedContractPdfHtml(
                            pdfModel,
                            signerName,
                            signerEmail,
                            now,
                            signatureDataUrl,
                            logoUrl
                        );

                        _logger.LogInformation("PDF HTML length={Len}. ContractId={ContractId}", pdfHtml.Length, contractId);

                        pdfBytes = await pdf.FromHtmlAsync(pdfHtml, $"Vertrag {contractNo}", ct);

                        _logger.LogInformation("PDF generated bytes={Bytes}. ContractId={ContractId}",
                            pdfBytes?.Length ?? 0, contractId);

                        if (pdfBytes is { Length: 0 }) pdfBytes = null;
                    }
                    catch (Exception exPdf)
                    {
                        pdfError = exPdf.ToString();
                        _logger.LogError(exPdf, "PDF generation failed. ContractId={ContractId}", contractId);
                        pdfBytes = null;
                    }

                    var msg = new EmailMessage
                    {
                        From = new EmailAddress("placeholder@local", "placeholder"),
                        Subject = subject,
                        HtmlBody = html,
                        TextBody = $"Vertrag unterschrieben. Link: {actionUrl}",
                        Bcc = new List<EmailAddress> { new EmailAddress(recipientEmail, signerName) },
                        Attachments = pdfBytes is null
                            ? new List<EmailAttachment>()
                            : new List<EmailAttachment>
                            {
                        new EmailAttachment($"Vertrag-{contractNo}.pdf", "application/pdf", pdfBytes)
                            }
                    };

#if DEBUG
                    if (pdfBytes is null && !string.IsNullOrWhiteSpace(pdfError))
                    {
                        msg.Attachments.Add(new EmailAttachment(
                            "pdf-error.txt",
                            "text/plain; charset=utf-8",
                            System.Text.Encoding.UTF8.GetBytes(pdfError)
                        ));
                    }
#endif

                    _logger.LogInformation("Sending ContractSigned email. Attachments={Count}. ContractId={ContractId}",
                        msg.Attachments?.Count ?? 0, contractId);

                    await emailSender.SendAsync(msg, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ContractSigned background job failed. ContractId={ContractId}", contractId);
                }
            });

            return new JsonResult(new { ok = true, signedAtIso = now.UtcDateTime.ToString("o") });
        }
        private static string BuildSignedContractPdfHtml(
    ContractPdfHtmlBuilder.ContractPdfDocumentModel model,
    string signerName,
    string signerEmail,
    DateTimeOffset signedAt,
    string signatureDataUrl,
    string logoUrl)
        {
            var html = ContractPdfHtmlBuilder.Build(model);

            html = html.Replace("__NETWITCHER_LOGO__", logoUrl ?? "", StringComparison.OrdinalIgnoreCase);

            var extraStyle = """
<style>
  .signedContractBlock{
    margin: 24px 0 0 0;
    page-break-inside: avoid;
    break-inside: avoid;
  }

  .signedContractCard{
    border: 1px solid #d7c7f3;
    border-radius: 18px;
    padding: 18px 20px;
    background: linear-gradient(180deg, #ffffff, #faf5ff);
  }

  .signedContractTitle{
    font-size: 18px;
    font-weight: 800;
    color: #2e1065;
    margin: 0 0 14px 0;
  }

  .signedContractRow{
    margin: 6px 0;
    font-size: 12.5px;
    line-height: 1.6;
    color: #31263f;
  }

  .signedContractRow strong{
    display: inline-block;
    min-width: 120px;
    color: #6b21a8;
  }

  .signedContractImage{
    margin-top: 14px;
  }

  .signedContractImage img{
    max-width: 260px;
    max-height: 120px;
    display: block;
  }

  .signedContractLine{
    width: 260px;
    border-top: 1px solid #7c3aed;
    margin-top: 8px;
  }
</style>
""";

            var signatureBlock = $"""
<div class="signedContractBlock">
  <div class="signedContractCard">
    <h2 class="signedContractTitle">Kundenunterschrift</h2>
    <div class="signedContractRow"><strong>Vertrag:</strong> {WebUtility.HtmlEncode(model.ContractNo)}</div>
    <div class="signedContractRow"><strong>Projekt:</strong> {WebUtility.HtmlEncode(model.ProjectTitle)}</div>
    <div class="signedContractRow"><strong>Name:</strong> {WebUtility.HtmlEncode(signerName ?? "")}</div>
    <div class="signedContractRow"><strong>E-Mail:</strong> {WebUtility.HtmlEncode(signerEmail ?? "")}</div>
    <div class="signedContractRow"><strong>Signiert am:</strong> {WebUtility.HtmlEncode(signedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))}</div>

    <div class="signedContractImage">
      <img src="{WebUtility.HtmlEncode(signatureDataUrl ?? "")}" alt="Signature" />
      <div class="signedContractLine"></div>
    </div>
  </div>
</div>
""";

            if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</head>", extraStyle + "</head>", StringComparison.OrdinalIgnoreCase);
            else
                html = extraStyle + html;

            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</body>", signatureBlock + "</body>", StringComparison.OrdinalIgnoreCase);
            else
                html += signatureBlock;

            return html;
        }

        // (نفس BuildRequestFromDb + NormalizeNewLines عندك)
        private GenerateContractDocumentRequest BuildRequestFromDb(Contract contract) { /* keep your existing */ throw new NotImplementedException(); }
        private static string NormalizeNewLines(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n");
        public async Task<IActionResult> OnPostSendInvoiceAsync(Guid invoiceId, CancellationToken ct)
        {
            if (invoiceId == Guid.Empty)
                return new JsonResult(new { ok = false, message = "Invalid invoice id." }) { StatusCode = 400 };

            try
            {
                var lex = HttpContext.RequestServices.GetRequiredService<LexwareInvoiceSyncService>();
                await lex.SendManualInvoiceAsync(invoiceId, ct);

                return new JsonResult(new { ok = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual invoice send failed. InvoiceId={InvoiceId}", invoiceId);
                return new JsonResult(new { ok = false, message = ex.Message }) { StatusCode = 500 };
            }
        }

        private ContractPdfHtmlBuilder.ContractPdfDocumentModel BuildContractPdfModel(
    Contract contract,
    bool showSignaturePlaceholder = true,
    string? notesText = "")
        {
            var customer = contract.Project.Customer;

            var providerLines = NormalizeNewLines(_opt.ProviderBlock ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var providerName = providerLines.FirstOrDefault() ?? "Netwitcher";
            var providerInfoHtml = string.Join("", providerLines.Skip(1)
                .Select(x => $"<div>{WebUtility.HtmlEncode(x)}</div>"));

            var customerName = customer.Type == CustomerType.Individual
                ? BuildName(customer.FirstName, customer.LastName, customer.Name)
                : (customer.Name ?? string.Empty).Trim();

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

            var customerInfoParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(addr?.StreetRaw))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(addr.StreetRaw)}</div>");

            var cityLine = ((addr?.PostalCode ?? "").Trim() + " " + (addr?.City ?? "").Trim()).Trim();
            if (!string.IsNullOrWhiteSpace(cityLine))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(cityLine)}</div>");

            if (!string.IsNullOrWhiteSpace(addr?.Country))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(addr.Country)}</div>");

            if (!string.IsNullOrWhiteSpace(email))
                customerInfoParts.Add($"<div>{WebUtility.HtmlEncode(email.Trim())}</div>");

            var structured = DeserializeStructured(contract.TermsStructured);

            var introMarkdown = ExtractMarkdownSection(contract.Terms, "## Vertragsgegenstand", "## Anlage A");
            if (string.IsNullOrWhiteSpace(introMarkdown))
            {
                introMarkdown =
                    "Der Anbieter erbringt die für das genannte Projekt vereinbarten Leistungen gemäß Anlage A – Leistungsbeschreibung.";
            }

            var introHtml = MarkdownToSafeHtml(introMarkdown);

            var servicesHtml = structured is not null && structured.Positions is not null && structured.Positions.Count > 0
                ? BuildServicesSectionHtml(structured, contract.Currency ?? "EUR")
                : MarkdownToSafeHtml(
                    ExtractMarkdownSection(contract.Terms, "## Anlage A", "## Preisübersicht"),
                    "<p>Die vereinbarten Leistungen sind in den Vertragspositionen festgehalten.</p>");

            var priceBoxHtml = BuildPriceBoxHtml(contract);

            var (netTotal, taxTotal, grossTotal) = CalculateContractTotals(contract);

            var dateRange = BuildDateRangeText(contract.StartDate, contract.EndDate);
            var totalDisplay = FormatMoney(contract.Currency, grossTotal);

            return new ContractPdfHtmlBuilder.ContractPdfDocumentModel
            {
                ContractId = contract.Id,
                ProjectId = contract.ProjectId,
                ContractNo = contract.ContractNo ?? "",
                Currency = contract.Currency ?? "EUR",
                StatusText = contract.Status.ToString(),
                ProjectTitle = contract.Project?.Title ?? "",
                CreatedAt = contract.CreatedAt,
                StartDate = contract.StartDate,
                EndDate = contract.EndDate,
                SummaryText = "Dieser Vertrag regelt die vereinbarten Leistungen, Zuständigkeiten und Konditionen für das genannte Projekt.",
                DateRangeText = dateRange,
                TotalAmountDisplay = totalDisplay,
                NotesText = notesText ?? "",
                Provider = new ContractPdfHtmlBuilder.ContractPdfParty
                {
                    Name = providerName,
                    InfoHtml = providerInfoHtml
                },
                Customer = new ContractPdfHtmlBuilder.ContractPdfParty
                {
                    Name = customerName,
                    InfoHtml = string.Join("", customerInfoParts)
                },
                ContractIntroHtml = introHtml,
                ServicesSectionHtml = servicesHtml,
                PriceBoxHtml = priceBoxHtml,
                ShowSignaturePlaceholder = showSignaturePlaceholder
            };
        }

        private static ContractStructuredTermsDto? DeserializeStructured(JsonDocument? doc)
        {
            if (doc is null) return null;

            try
            {
                return JsonSerializer.Deserialize<ContractStructuredTermsDto>(
                    doc.RootElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        private static string BuildServicesSectionHtml(ContractStructuredTermsDto structured, string currency)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var sb = new StringBuilder();

            foreach (var p in (structured.Positions ?? new List<ContractPositionSpecDto>()).OrderBy(x => x.PositionNo))
            {
                var title = string.IsNullOrWhiteSpace(p.Title)
                    ? $"Position {p.PositionNo}"
                    : p.Title.Trim();

                var price = p.LineNetPrice.HasValue
                    ? $"{p.LineNetPrice.Value.ToString("N2", de)} {currency}"
                    : "";

                sb.AppendLine("""<div class="contract-pos">""");
                sb.AppendLine("""<div class="contract-pos__head">""");
                sb.AppendLine($"""<h3>Position {p.PositionNo}: {WebUtility.HtmlEncode(title)}</h3>""");

                if (!string.IsNullOrWhiteSpace(price))
                    sb.AppendLine($"""<div class="contract-pos__price">{WebUtility.HtmlEncode(price)}</div>""");

                sb.AppendLine("""</div>""");

                if (!string.IsNullOrWhiteSpace(p.Sections?.Scope))
                {
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Scope.Trim()) + "</p>");
                }

                AppendHtmlListSection(sb, "Liefergegenstände", p.Sections?.Deliverables);
                AppendHtmlListSection(sb, "Nicht enthalten", p.Sections?.OutOfScope);
                AppendHtmlListSection(sb, "Mitwirkungspflichten des Auftraggebers", p.Sections?.CustomerResponsibilities);
                AppendHtmlListSection(sb, "Abnahmekriterien", p.Sections?.AcceptanceCriteria);

                if (!string.IsNullOrWhiteSpace(p.Sections?.Timeline))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Zeitplan</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Timeline.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Assumptions))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Annahmen</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Assumptions.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Revisions))
                {
                    sb.AppendLine("<section>");
                    sb.AppendLine("<h4>Überarbeitungen</h4>");
                    sb.AppendLine("<p>" + WebUtility.HtmlEncode(p.Sections.Revisions.Trim()) + "</p>");
                    sb.AppendLine("</section>");
                }

                sb.AppendLine("</div>");
            }

            return sb.Length == 0
                ? "<p>Keine Leistungspositionen vorhanden.</p>"
                : sb.ToString();
        }

        private static void AppendHtmlListSection(StringBuilder sb, string title, List<string>? items)
        {
            var clean = (items ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (clean.Count == 0) return;

            sb.AppendLine("<section>");
            sb.AppendLine($"<h4>{WebUtility.HtmlEncode(title)}</h4>");
            sb.AppendLine("<ul>");

            foreach (var item in clean)
                sb.AppendLine("<li>" + WebUtility.HtmlEncode(item) + "</li>");

            sb.AppendLine("</ul>");
            sb.AppendLine("</section>");
        }

        private string BuildPriceBoxHtml(Contract contract)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var currency = string.IsNullOrWhiteSpace(contract.Currency) ? "EUR" : contract.Currency.Trim();

            var rows = (contract.Items ?? new List<ContractItem>())
                .OrderBy(x => x.Position)
                .Select(x => new
                {
                    Position = x.Position,
                    Title = string.IsNullOrWhiteSpace(x.Title) ? $"Position {x.Position}" : x.Title.Trim(),
                    Net = ResolveContractItemNetAmount(x)
                })
                .ToList();

            var (netTotal, taxTotal, grossTotal) = CalculateContractTotals(contract);

            var sb = new StringBuilder();
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Pos.</th><th>Bezeichnung</th><th>Netto</th></tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var row in rows)
            {
                sb.AppendLine($"""
<tr>
  <td>{row.Position}</td>
  <td>{WebUtility.HtmlEncode(row.Title)}</td>
  <td>{WebUtility.HtmlEncode(row.Net.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</td>
</tr>
""");
            }

            sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>Zwischensumme (Netto)</strong></td>
  <td><strong>{WebUtility.HtmlEncode(netTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");

            if (taxTotal > 0m)
            {
                sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>USt. 19%</strong></td>
  <td><strong>{WebUtility.HtmlEncode(taxTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");
            }

            sb.AppendLine($"""
<tr>
  <td></td>
  <td><strong>Gesamtbetrag</strong></td>
  <td><strong>{WebUtility.HtmlEncode(grossTotal.ToString("N2", de))} {WebUtility.HtmlEncode(currency)}</strong></td>
</tr>
""");

            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");

            sb.AppendLine("<p style=\"margin-top:10px;color:#5b556a;font-size:12px;\">");
            sb.AppendLine("Alle Beträge netto zzgl. gesetzlicher Umsatzsteuer, sofern nicht anders ausgewiesen.");
            sb.AppendLine("</p>");

            return sb.ToString();
        }

        private static (decimal Net, decimal Tax, decimal Gross) CalculateContractTotals(Contract contract)
        {
            var net = (contract.Items ?? new List<ContractItem>())
                .Sum(ResolveContractItemNetAmount);

            net = Math.Round(net, 2, MidpointRounding.AwayFromZero);

            var tax = contract.ApplyVat
                ? Math.Round(net * 0.19m, 2, MidpointRounding.AwayFromZero)
                : 0m;

            var gross = net + tax;
            return (net, tax, gross);
        }

        private static decimal ResolveContractItemNetAmount(ContractItem item)
        {
            if (item.AgreedPrice.HasValue && item.AgreedPrice.Value > 0m)
                return Math.Round(item.AgreedPrice.Value, 2, MidpointRounding.AwayFromZero);

            var total = ReadDecimal(item.PriceBreakdown, "total", 0m);
            if (total > 0m)
                return Math.Round(total, 2, MidpointRounding.AwayFromZero);

            var subTotal = ReadDecimal(item.PriceBreakdown, "subTotal", 0m);
            if (subTotal > 0m)
                return Math.Round(subTotal, 2, MidpointRounding.AwayFromZero);

            var fallback = item.Quantity * item.UnitPrice;
            return Math.Round(fallback, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal ReadDecimal(JsonDocument? doc, string prop, decimal fallback)
        {
            try
            {
                if (doc is null) return fallback;
                if (!doc.RootElement.TryGetProperty(prop, out var value)) return fallback;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d))
                    return d;

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildDateRangeText(DateOnly? start, DateOnly? end)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            if (start.HasValue && end.HasValue)
                return $"{start.Value.ToString("dd.MM.yyyy", de)} – {end.Value.ToString("dd.MM.yyyy", de)}";

            if (start.HasValue)
                return $"ab {start.Value.ToString("dd.MM.yyyy", de)}";

            if (end.HasValue)
                return $"bis {end.Value.ToString("dd.MM.yyyy", de)}";

            return "—";
        }

        private static string FormatMoney(string? currency, decimal value)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim();

            return string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)
                ? value.ToString("N2", de) + " €"
                : value.ToString("N2", de) + " " + currency;
        }

        private static string ExtractMarkdownSection(string? markdown, string startHeading, string? nextHeading)
        {
            markdown = NormalizeNewLines(markdown ?? "");

            if (string.IsNullOrWhiteSpace(markdown))
                return "";

            var start = markdown.IndexOf(startHeading, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";

            start = markdown.IndexOf('\n', start);
            if (start < 0) return "";

            start++;

            var end = !string.IsNullOrWhiteSpace(nextHeading)
                ? markdown.IndexOf(nextHeading, start, StringComparison.OrdinalIgnoreCase)
                : -1;

            if (end < 0) end = markdown.Length;

            return markdown.Substring(start, end - start).Trim();
        }

        private static string MarkdownToSafeHtml(string markdown, string? fallbackHtml = null)
        {
            markdown = NormalizeNewLines(markdown ?? "").Trim();

            if (string.IsNullOrWhiteSpace(markdown))
                return fallbackHtml ?? "<p>—</p>";

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(markdown, pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");

            return sanitizer.Sanitize(html);
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

        private static string ExtractRenderableHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var styleBlock = ExtractStyleBlock(html);
            var bodyBlock = ExtractBodyBlock(html);
            var scopedStyleBlock = ScopeContractStyleBlock(styleBlock, ".contractPdfScope");

            return scopedStyleBlock + bodyBlock;
        }

        private static string ScopeContractStyleBlock(string htmlStyleBlock, string scopeSelector)
        {
            if (string.IsNullOrWhiteSpace(htmlStyleBlock))
                return string.Empty;

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
            if (start < 0) return string.Empty;

            var openEnd = html.IndexOf('>', start);
            if (openEnd < 0) return string.Empty;

            var close = html.IndexOf("</style>", openEnd, StringComparison.OrdinalIgnoreCase);
            if (close < 0) return string.Empty;

            return html.Substring(start, (close + "</style>".Length) - start);
        }

        private static string ExtractBodyBlock(string html)
        {
            var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStart < 0) return html;

            bodyStart = html.IndexOf('>', bodyStart);
            if (bodyStart < 0) return html;

            bodyStart++;

            var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyEnd < 0 || bodyEnd <= bodyStart)
                return html.Substring(bodyStart);

            return html.Substring(bodyStart, bodyEnd - bodyStart);
        }
    }
}

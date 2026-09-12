using Ganss.Xss;
using WitcherHub.Rendering;
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

        /// <summary>
        /// Where the "Vertrag" link in the consent sentence points.
        ///
        /// It used to point at the public privacy policy — a link labelled
        /// "Vertrag" that opened the Datenschutzerklärung. The contract's own
        /// clauses are now on this page, so it points at them; if a contract has
        /// no clauses beyond its subject matter, it points at the document.
        /// </summary>
        public string ContractTermsAnchor { get; private set; } = "#paper";

        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public string? SignatureDataUrl { get; private set; }

        public string? SignerNamePrefill { get; private set; }
        public string? SignerEmailPrefill { get; private set; }

        /// <summary>
        /// Why a signing link stopped working, to the person holding it.
        ///
        /// A bare 401 was the same answer for a mistyped link, an expired one and
        /// one that was deliberately revoked — and the third case is now the
        /// common one: approving new wording revokes the links issued for the old
        /// text, on purpose, so that nobody signs a document they were not sent.
        /// Telling the customer "not authorised" when the truth is "this contract
        /// has been revised, ask for a new link" leaves them believing something
        /// is broken.
        ///
        /// Nothing about the contract is disclosed: the reason is the same
        /// sentence whether or not the link ever existed.
        /// </summary>
        private async Task<IActionResult> LinkRefusedAsync(string tokenHash, CancellationToken ct)
        {
            var revised = await _db.ContractAccessLinks
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ContractId == Id &&
                    x.TokenHash == tokenHash &&
                    x.RevokedBecauseWordingChanged, ct);

            if (!revised) return Unauthorized();

            const string body = """
                <!doctype html><html lang="de"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Vertrag überarbeitet</title>
                <style>
                  body{margin:0;padding:48px 16px;background:#fff;color:#111;
                       font:16px/1.5 Arial,"Liberation Sans",sans-serif}
                  main{max-width:34rem;margin:0 auto}
                  h1{font-size:1.4rem;margin:0 0 1rem}
                  p{margin:0 0 1rem}
                </style></head><body><main>
                <h1>Dieser Vertrag wurde überarbeitet</h1>
                <p>Der Vertragstext, für den dieser Link versendet wurde, ist nicht mehr
                   die aktuelle Fassung. Der Link wurde deshalb deaktiviert, damit keine
                   Fassung unterschrieben wird, die Ihnen nicht zugesendet wurde.</p>
                <p>Bitte fordern Sie einen neuen Signaturlink an. Ihr bisheriger Stand
                   bleibt unverändert; es wurde nichts unterschrieben.</p>
                </main></body></html>
                """;

            return new ContentResult
            {
                Content = body,
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status410Gone
            };
        }

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

            if (link is null) return await LinkRefusedAsync(tokenHash, ct);

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
                .Include(c => c.Drafts)
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

            // The document that gets signed
            //
            // The approved version is the contract. It is used exactly as
            // approved and never rebuilt from the current catalog — a service
            // whose price changed after approval must not change what is being
            // signed. Only a contract with no approved wording at all falls back
            // to generating from positions.
            if (string.IsNullOrWhiteSpace(contract.Terms))
            {
                var approved = contract.Drafts
                    .Where(d => d.IsApproved)
                    .OrderByDescending(d => d.ApprovedAt)
                    .FirstOrDefault();

                if (approved is not null)
                {
                    contract.Terms = NormalizeNewLines(approved.DocumentMarkdown);
                }
                else if (contract.Items is { Count: > 0 })
                {
                    var req = BuildRequestFromDb(contract);
                    var doc = await _generator.GenerateAsync(req, ct);
                    contract.Terms = NormalizeNewLines(doc.FullDocument);
                }
                else
                {
                    return BadRequest(
                        "This contract has no approved wording and no positions, so there is nothing to sign yet.");
                }

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
            var model = ContractPdfDocument.Build(
                contract,
                _opt,
                showSignaturePlaceholder: false,
                notesText: "");

            var fullHtml = ContractPdfHtmlBuilder.Build(model);

            var logoPath = Url.Content("~/img/netwitcher-logo.png")
                          ?? "/img/netwitcher-logo.png";

            var logoUrl = $"{Request.Scheme}://{Request.Host}{logoPath}";

            fullHtml = fullHtml.Replace("__NETWITCHER_LOGO__", logoUrl, StringComparison.OrdinalIgnoreCase);

            ContractHtml = ExtractRenderableHtml(fullHtml);

            if (ContractHtml.Contains("id=\"vertragsbedingungen\"", StringComparison.Ordinal))
                ContractTermsAnchor = "#vertragsbedingungen";

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
                return new JsonResult(new { ok = false, message = "The signature could not be read. Please draw it again." }) { StatusCode = 400 };

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
                if (!exists) return new JsonResult(new { ok = false, message = "This contract could not be found. The link may be out of date." }) { StatusCode = 404 };
                return new JsonResult(new { ok = false, message = "This contract has already been signed." }) { StatusCode = 409 };
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

            // Record which text was signed and what it hashed to.
            //
            // Without this, "the signed contract" means "whatever the contract
            // says today", and a later edit quietly changes what somebody is held
            // to. The hash is over the exact terms as they stood at this moment.
            var signedTerms = await _db.Contracts
                .Where(c => c.Id == Id)
                .Select(c => c.Terms)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(signedTerms))
            {
                var signedVersion = await _db.Set<ContractDraft>()
                    .Where(d => d.ContractId == Id && d.IsApproved)
                    .OrderByDescending(d => d.ApprovedAt)
                    .Select(d => (int?)d.Version)
                    .FirstOrDefaultAsync(ct);

                await _db.Contracts
                    .Where(c => c.Id == Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.SignedDocumentHash, ContractDraftService.Sha256(signedTerms))
                        .SetProperty(c => c.SignedDraftVersion, signedVersion),
                        ct);
            }

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

            // Captured here rather than reached for inside the background job:
            // the page instance is gone by the time that runs.
            var templateOptions = _opt;

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
                        var pdfModel = ContractPdfDocument.Build(
                            info,
                            templateOptions,
                            showSignaturePlaceholder: false,
                            notesText: "");

                        var logoUrl = $"{baseUrl}/img/netwitcher-logo.png";

                        var pdfHtml = ContractPdfHtmlBuilder.BuildSigned(
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

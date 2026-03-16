using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Lexware;
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
            var markdown = NormalizeNewLines(contract.Terms ?? "");
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(markdown, pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");
            ContractHtml = sanitizer.Sanitize(html);

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
                        .FirstOrDefaultAsync(c => c.Id == contractId, token);

                    if (info is null)
                    {
                        _logger.LogWarning("ContractSigned email skipped: Contract not found. ContractId={ContractId}", contractId);
                        return;
                    }

                    var contractNo = info.ContractNo ?? contractId.ToString();
                    var projectTitle = info.Project?.Title ?? "Project";
                    var termsMarkdown = info.Terms ?? "";

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
                        var pdfHtml = BuildSignedPdfHtml(
                            contractNo: contractNo,
                            projectTitle: projectTitle,
                            termsMarkdown: termsMarkdown,
                            signerName: signerName,
                            signerEmail: signerEmail,
                            signedAt: now,
                            signatureDataUrl: signatureDataUrl
                        );

                        _logger.LogInformation("PDF HTML length={Len}. ContractId={ContractId}", pdfHtml.Length, contractId);

                        pdfBytes = pdf.FromHtml(pdfHtml, $"Vertrag {contractNo}");

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
        private static string BuildSignedPdfHtml(
            string contractNo,
            string projectTitle,
            string termsMarkdown,
            string signerName,
            string signerEmail,
            DateTimeOffset signedAt,
            string signatureDataUrl)
        {
            termsMarkdown ??= "";
            termsMarkdown = NormalizeNewLines(termsMarkdown);

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var body = Markdown.ToHtml(termsMarkdown, pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");
            body = sanitizer.Sanitize(body);

            var css = """
<style>
  @page { size: A4; margin: 12mm 12mm 12mm 12mm; }
  body{font-family:Arial,Helvetica,sans-serif;color:#111;font-size:12.5px;line-height:1.55;}
  h1{font-size:20px;margin:0 0 10px;page-break-after:avoid;}
  h2{font-size:16px;margin:16px 0 8px;page-break-after:avoid;}
  h3{font-size:14px;margin:12px 0 6px;page-break-after:avoid;}
  p{margin:0 0 8px;}
  hr{margin:14px 0;border:0;border-top:1px solid #ddd;}
  table{width:100%;border-collapse:collapse;margin:10px 0;}
  th,td{border:1px solid #ddd;padding:6px;vertical-align:top;}
  th{background:#f3f3f3;font-weight:700;}
  /* ✅ لا تفصل كتلة التوقيع */
  .sigWrap{break-inside:avoid;page-break-inside:avoid;}
  .meta{margin-bottom:10px;font-size:12px;color:#333;}
  .sig img{max-width:260px;height:auto;display:block;margin-top:6px;}
  .sigLine{margin-top:6px;border-top:1px solid #111;width:260px;}
</style>
""";

            var sigBlock = $"""
<hr/>
<div class="sigWrap">
  <h2>Unterschrift</h2>
  <div class="meta">
    <div><strong>Vertragsnummer:</strong> {System.Net.WebUtility.HtmlEncode(contractNo)}</div>
    <div><strong>Projekt:</strong> {System.Net.WebUtility.HtmlEncode(projectTitle)}</div>
    <div><strong>Name:</strong> {System.Net.WebUtility.HtmlEncode(signerName)}</div>
    <div><strong>E-Mail:</strong> {System.Net.WebUtility.HtmlEncode(signerEmail)}</div>
    <div><strong>Datum:</strong> {System.Net.WebUtility.HtmlEncode(signedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))}</div>
  </div>
  <div class="sig">
    <img src="{signatureDataUrl}" />
    <div class="sigLine"></div>
  </div>
</div>
""";

            return $"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  {css}
</head>
<body>
  {body}
  {sigBlock}
</body>
</html>
""";
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
    }
}

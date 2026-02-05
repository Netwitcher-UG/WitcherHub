using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    public class SignModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IContractDocumentGenerator _generator;
        private readonly ContractTemplateOptions _opt;

        public SignModel(AppDbContext db, IContractDocumentGenerator generator, IOptions<ContractTemplateOptions> opt)
        {
            _db = db;
            _generator = generator;
            _opt = opt.Value;
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
                    return BadRequest("Contract has no line items.");

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

        public async Task<IActionResult> OnPostSignAsync([FromQuery(Name = "t")] string? t, CancellationToken ct)
        {
            if (Id == Guid.Empty) return new JsonResult(new { ok = false, message = "Invalid contract id." }) { StatusCode = 400 };
            if (string.IsNullOrWhiteSpace(t)) return new JsonResult(new { ok = false, message = "Unauthorized." }) { StatusCode = 401 };

            Token = t;
            var tokenHash = ContractAccessLink.HashToken(Token.Trim());
            var linkOk = await _db.ContractAccessLinks.AnyAsync(x =>
                x.ContractId == Id &&
                x.TokenHash == tokenHash &&
                x.RevokedAtUtc == null &&
                x.ExpiresAt > DateTimeOffset.UtcNow, ct);

            if (!linkOk) return new JsonResult(new { ok = false, message = "Unauthorized." }) { StatusCode = 401 };

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

            return new JsonResult(new { ok = true, signedAtIso = now.UtcDateTime.ToString("o") });
        }

        // (نفس BuildRequestFromDb + NormalizeNewLines عندك)
        private GenerateContractDocumentRequest BuildRequestFromDb(Contract contract) { /* keep your existing */ throw new NotImplementedException(); }
        private static string NormalizeNewLines(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n");
    }
}

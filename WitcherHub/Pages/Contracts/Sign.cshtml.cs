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

        public string ContractHtml { get; private set; } = "";
        public string ProviderName { get; private set; } = "";
        public string ProviderAddress { get; private set; } = "";

        // Existing signature (if already signed)
        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public string? SignatureDataUrl { get; private set; }

        // Prefill customer fields
        public string? SignerNamePrefill { get; private set; }
        public string? SignerEmailPrefill { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            var contract = await _db.Contracts
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Addresses)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Items)
                    .ThenInclude(i => i.Service)
                .Include(c => c.Signatures)
                .FirstOrDefaultAsync(c => c.Id == Id, ct);

            if (contract is null) return NotFound();

            // Provider block split
            var pb = NormalizeNewLines(_opt.ProviderBlock ?? "");
            var lines = pb.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            ProviderName = lines.Length > 0 ? lines[0] : "";
            ProviderAddress = lines.Length > 1 ? string.Join("\n", lines.Skip(1)) : "";

            // Prefill from customer
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
            SignerEmailPrefill = email;

            // If not generated yet: generate once and store in Contract.Terms
            if (string.IsNullOrWhiteSpace(contract.Terms))
            {
                var req = BuildRequestFromDb(contract);
                if (contract.Items == null || contract.Items.Count == 0)
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Line items required";
                    TempData["Toast.Message"] = "Please add at least one line item before signing.";

                    return RedirectToPage("/Contracts/Items/Create", new { contractId = contract.Id });
                }
                var doc = await _generator.GenerateAsync(req, ct);

                contract.Terms = NormalizeNewLines(doc.FullDocument);

                // optional: when generated we can mark as Sent later when you implement "Send"
                if (contract.Status == DocumentStatus.Draft)
                    contract.Status = DocumentStatus.Draft;

                await _db.SaveChangesAsync(ct);
            }

            // Existing signature (latest)
            var sig = contract.Signatures
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            if (sig is not null && sig.SignedAt is not null)
            {
                IsSigned = true;
                SignedAtIso = sig.SignedAt.Value.UtcDateTime.ToString("o");

                if (sig.SignatureData is not null &&
                    sig.SignatureData.RootElement.TryGetProperty("dataUrl", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    SignatureDataUrl = p.GetString();
                }

                // prefer signed name/email if present
                if (!string.IsNullOrWhiteSpace(sig.SignerName)) SignerNamePrefill = sig.SignerName;
                if (!string.IsNullOrWhiteSpace(sig.SignerEmail)) SignerEmailPrefill = sig.SignerEmail;
            }

            // Markdown -> HTML
            var markdown = NormalizeNewLines(contract.Terms ?? "");
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(markdown, pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");
            ContractHtml = sanitizer.Sanitize(html);

            return Page();
        }

        public async Task<IActionResult> OnPostSignAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return new JsonResult(new { ok = false, message = "Invalid contract id." }) { StatusCode = 400 };

            var signerName = (Request.Form["SignerName"].ToString() ?? "").Trim();
            var signerEmail = (Request.Form["SignerEmail"].ToString() ?? "").Trim();
            var signatureDataUrl = (Request.Form["SignatureDataUrl"].ToString() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(signerName))
                return new JsonResult(new { ok = false, message = "Signer name is required." }) { StatusCode = 400 };

            if (string.IsNullOrWhiteSpace(signatureDataUrl) || !signatureDataUrl.StartsWith("data:image/"))
                return new JsonResult(new { ok = false, message = "Invalid signature data." }) { StatusCode = 400 };

            var contract = await _db.Contracts
                .Include(c => c.Signatures)
                .FirstOrDefaultAsync(c => c.Id == Id, ct);

            if (contract is null)
                return new JsonResult(new { ok = false, message = "Contract not found." }) { StatusCode = 404 };

            if (contract.Status == DocumentStatus.Signed || contract.SignedAt is not null)
                return new JsonResult(new { ok = false, message = "Contract already signed." }) { StatusCode = 409 };

            var now = DateTimeOffset.UtcNow;

            var payload = JsonSerializer.SerializeToDocument(new
            {
                dataUrl = signatureDataUrl,
                userAgent = Request.Headers.UserAgent.ToString(),
                signedAt = now.UtcDateTime.ToString("o")
            });

            contract.Signatures.Add(new ContractSignature
            {
                ContractId = contract.Id,
                SignerName = signerName,
                SignerEmail = string.IsNullOrWhiteSpace(signerEmail) ? null : signerEmail,
                SignedAt = now,
                SignatureData = payload
            });

            contract.SignedAt = now;
            contract.Status = DocumentStatus.Signed;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new
            {
                ok = true,
                signedAtIso = now.UtcDateTime.ToString("o")
            });
        }

        private GenerateContractDocumentRequest BuildRequestFromDb(Contract contract)
        {
            var project = contract.Project;
            var customer = project.Customer;

            var billing = customer.Addresses?
                .OrderByDescending(a => a.IsDefault)
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

            var customerBlock =
                $"Name/Firma: {customer.Name}\n" +
                $"Adresse: {(billing?.StreetRaw ?? "")} {(billing?.AddressLine2 ?? "")}\n" +
                $"PLZ/Ort: {(billing?.PostalCode ?? "")} {(billing?.City ?? "")}\n" +
                (string.IsNullOrWhiteSpace(email) ? "" : $"E-Mail: {email}\n");

            var lines = contract.Items
                .OrderBy(i => i.Position)
                .Select(i => new ContractServiceLineDto
                {
                    Position = i.Position,
                    Title = i.Title,
                    ServiceName = i.Service?.Name,
                    ServiceType = i.Service?.ServiceType.ToString(),
                    PricingModel = i.Service?.PricingModel.ToString(),
                    AgreedPrice = i.AgreedPrice,
                    Config = i.Config is null
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(i.Config.RootElement.GetRawText()) ?? new()
                })
                .ToList();

            return new GenerateContractDocumentRequest
            {
                ContractNo = contract.ContractNo,
                ProjectTitle = project.Title,
                Currency = contract.Currency ?? "EUR",
                StartDate = contract.StartDate,
                EndDate = contract.EndDate,
                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,
                CustomerBlockOverride = customerBlock,
                Services = lines,
                SignerName = "" // will be filled by user on page
            };
        }

        private static string NormalizeNewLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n");
        }
    }
}

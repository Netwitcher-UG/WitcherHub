using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;  
using WitcherHub.Infrastructure.Services.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IContractDocumentGenerator _generator;
        private readonly ContractTemplateOptions _opt;
        private readonly IContractDraftService _drafts;

        public DetailsModel(
            AppDbContext db,
            IContractDocumentGenerator generator,
            IContractDraftService drafts,
            IOptions<ContractTemplateOptions> opt)
        {
            _db = db;
            _generator = generator;
            _drafts = drafts;
            _opt = opt.Value;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public string ContractHtml { get; private set; } = "";

        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid ContractId => Id;
        public Contract? Contract { get; private set; }
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
            ProjectId = contract.ProjectId;
            Contract = contract;
            // Generate terms once if missing (نفس Sign)
            // Generate terms once if missing (same as Sign, but must have line items)
            // ✅ بدل التوليد التلقائي:
            // A contract needs positions or contract text, not positions
            // specifically. Sending a supplied-text contract back to the position
            // builder — which is where it was already finished — was the same
            // wrong rule that blocked generation.
            var source = await _drafts.GetSourceAsync(contract.Id, ct);

            if (!source.CanGenerate)
            {
                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Nothing to show yet";
                TempData["Toast.Message"] = source.BlockingReason;

                return RedirectToPage("/Contracts/Positions", new { contractId = contract.Id });
            }

            if (string.IsNullOrWhiteSpace(contract.Terms))
            {
                TempData["Toast.Type"] = "info";
                TempData["Toast.Title"] = "Not generated";
                TempData["Toast.Message"] = "Contract terms are not generated yet. Click Update Contract from the project.";

                // ✅ لا توليد هنا أبداً
                // خليه يكمل يعرض صفحة فيها رسالة بدل HTML
                ContractHtml = "<div class='alert alert-info'>Contract is not generated yet.</div>";
                return Page();
            }


            // Signed status
            var sig = contract.Signatures
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            if (sig is not null && sig.SignedAt is not null)
            {
                IsSigned = true;
                SignedAtIso = sig.SignedAt.Value.UtcDateTime.ToString("o");
            }
            else
            {
                // fallback based on status
                IsSigned = contract.Status == DocumentStatus.Signed;
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
                        : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(i.Config.RootElement.GetRawText()) ?? new()
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
                SignerName = ""
            };
        }

        private static string NormalizeNewLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n");
        }
    }
}

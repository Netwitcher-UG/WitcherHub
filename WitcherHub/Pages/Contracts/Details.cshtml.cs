using WitcherHub.Rendering;
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
        private readonly IOptions<WitcherHub.Infrastructure.Services.Pdf.BrandingOptions> _branding;
        private readonly IWebHostEnvironment _env;

        public DetailsModel(
            AppDbContext db,
            IContractDocumentGenerator generator,
            IContractDraftService drafts,
            IOptions<ContractTemplateOptions> opt,
            IOptions<WitcherHub.Infrastructure.Services.Pdf.BrandingOptions> branding,
            IWebHostEnvironment env)
        {
            _db = db;
            _generator = generator;
            _drafts = drafts;
            _opt = opt.Value;
            _branding = branding;
            _env = env;
        }

        /// <summary>
        /// The company's mark and the document's reference, for the top of the
        /// sheet. Built here rather than in the view so the view does not have to
        /// know where the logo file lives, or whether it is there at all.
        /// </summary>
        public WitcherHub.Pages.Models.UI.ContractLetterheadVm Letterhead { get; private set; } =
            new();

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        /// <summary>
        /// A specific version to show instead of the approved wording.
        ///
        /// Without this, the page could only render <c>contract.Terms</c> — which
        /// is set by approval — so the owner was asked to approve wording they
        /// had never been able to read. Reading has to come before agreeing.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public int? Version { get; set; }

        public string ContractHtml { get; private set; } = "";

        public bool IsSigned { get; private set; }
        public string? SignedAtIso { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid ContractId => Id;
        public Contract? Contract { get; private set; }

        /// <summary>True when a version is being previewed rather than the approved wording.</summary>
        public bool IsPreview => Version is not null;

        /// <summary>The previewed version's standing, for the banner.</summary>
        public string? PreviewStatusLabel { get; private set; }

        /// <summary>True when the previewed version can be approved from here.</summary>
        public bool PreviewCanApprove { get; private set; }

        /// <summary>
        /// The versions this contract has, for the empty state: a contract with
        /// unapproved wording is not "not generated", it is "not approved yet",
        /// and the difference is a link away from being resolved.
        /// </summary>
        public IReadOnlyList<ContractDraftSummary> Versions { get; private set; } =
            Array.Empty<ContractDraftSummary>();
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

            Letterhead = WitcherHub.Pages.Models.UI.ContractLetterheadVm.Build(
                _branding,
                _env.WebRootPath,
                Request.PathBase,
                companyName: FirstLine(_opt.ProviderBlock),
                contractNo: contract.ContractNo,
                issuedOn: contract.CreatedAt);
            // Generate terms once if missing (نفس Sign)
            // Generate terms once if missing (same as Sign, but must have line items)
            // ✅ بدل التوليد التلقائي:
            // A contract needs positions or contract text, not positions
            // specifically. Sending a supplied-text contract back to the position
            // builder — which is where it was already finished — was the same
            // wrong rule that blocked generation.
            // A version asked for by number is shown regardless of what stands
            // approved. This is how wording is read before it is agreed to; the
            // banner in the view says plainly that it is a preview.
            if (Version is int version)
            {
                var draft = await _drafts.GetDraftAsync(contract.Id, version, ct);

                if (draft is null || string.IsNullOrWhiteSpace(draft.DocumentMarkdown))
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "No such version";
                    TempData["Toast.Message"] = $"Version {version} has no wording to show.";

                    return RedirectToPage("/Contracts/Details", new { id = contract.Id });
                }

                PreviewStatusLabel = draft.StatusLabel;
                PreviewCanApprove = draft.Status is not ContractDraftStatus.Approved
                                                and not ContractDraftStatus.Signed;

                ContractHtml = RenderMarkdown(draft.DocumentMarkdown);

                return Page();
            }

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
                // Not a dead end. "Not generated yet" was the wrong diagnosis for
                // the common case — the wording exists as versions and simply has
                // not been approved — and the message gave the reader nothing to
                // click. The view now lists the versions with a preview link each.
                Versions = await _drafts.GetDraftsAsync(contract.Id, ct);

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

            ContractHtml = RenderMarkdown(contract.Terms ?? "");
            return Page();
        }

        /// <summary>Markdown to sanitised HTML, one way for every path on this page.</summary>
        private static string RenderMarkdown(string markdown) =>
            ContractMarkdown.ToHtml(markdown);

        /// <summary>
        /// The company's name out of the configured provider block, which is the
        /// name and then the address on the lines below it.
        /// </summary>
        private static string FirstLine(string? block) =>
            (block ?? "").Replace("\r\n", "\n").Split('\n').FirstOrDefault()?.Trim() ?? "";

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

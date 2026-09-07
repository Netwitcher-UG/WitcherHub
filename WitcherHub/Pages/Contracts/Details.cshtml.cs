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
using WitcherHub.Infrastructure.Services.Pdf;
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
        private readonly IPdfGenerator _pdf;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(
            AppDbContext db,
            IContractDocumentGenerator generator,
            IContractDraftService drafts,
            IOptions<ContractTemplateOptions> opt,
            IOptions<WitcherHub.Infrastructure.Services.Pdf.BrandingOptions> branding,
            IWebHostEnvironment env,
            IPdfGenerator pdf,
            ILogger<DetailsModel> logger)
        {
            _db = db;
            _generator = generator;
            _drafts = drafts;
            _opt = opt.Value;
            _branding = branding;
            _env = env;
            _pdf = pdf;
            _logger = logger;
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

        /// <summary>
        /// True once the contract is settled and its wording must not move.
        /// The same rule the handlers enforce, so the buttons drawn here and the
        /// answer to pressing them cannot disagree.
        /// </summary>
        public bool IsLocked => ContractLock.IsLocked(Contract);

        /// <summary>
        /// Why editing is refused, for the tooltip on the disabled action — and
        /// null when nothing is refused, so an unlocked page does not carry a
        /// sentence saying it is locked.
        /// </summary>
        public string? LockReason => IsLocked ? ContractLock.Reason(Contract!) : null;

        /// <summary>
        /// True when there is a signed contract to hand back.
        ///
        /// Deliberately stricter than <see cref="IsSigned"/>, which also reports
        /// a signature found on a contract whose status says otherwise. The
        /// download rebuilds a document headed "signed", so it is offered only
        /// where the status agrees — and where the handler will actually produce
        /// one, rather than offering a button that answers with an apology.
        /// </summary>
        public bool CanDownloadSignedPdf { get; private set; }

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

            // The download is offered only where it can be produced: a signed
            // status, and a signature carrying the drawn image the PDF puts on
            // the page. Asked through the same two helpers the handler uses, so
            // a button that appears is a button that works — the alternative is
            // one that answers with an apology.
            CanDownloadSignedPdf =
                contract.Status == DocumentStatus.Signed &&
                SignatureImageOf(SignedSignature(contract)) is not null;

            ContractHtml = RenderMarkdown(contract.Terms ?? "");
            return Page();
        }

        /// <summary>
        /// The contract as it was signed, with the customer's signature on it.
        ///
        /// Nothing stores this: the signing flow builds it, attaches it to the
        /// confirmation e-mail and lets it go. So it is rebuilt here from the
        /// same document and the same stored signature the e-mail used — the
        /// arrangement the quote's signed PDF already uses.
        /// </summary>
        public async Task<IActionResult> OnGetSignedPdfAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

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
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == Id, ct);

            if (contract is null) return NotFound();

            // Only a signed contract has a signed PDF. The button is only drawn
            // on one; this is the same rule where it is actually enforced, since
            // a URL can be typed and a page can be stale.
            var signature = SignedSignature(contract);

            if (contract.Status != DocumentStatus.Signed || signature is null)
                return SignedPdfUnavailable("This contract has not been signed yet.");

            var signatureDataUrl = SignatureImageOf(signature);

            if (signatureDataUrl is null)
                return SignedPdfUnavailable("The signature image for this contract could not be found.");

            try
            {
                var model = ContractPdfDocument.Build(
                    contract,
                    _opt,
                    showSignaturePlaceholder: false,
                    notesText: "");

                var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : "";
                var logoUrl = $"{Request.Scheme}://{Request.Host.ToUriComponent()}{pathBase}/img/netwitcher-logo.png";

                var html = ContractPdfHtmlBuilder.BuildSigned(
                    model,
                    signature.SignerName ?? "",
                    signature.SignerEmail ?? "",
                    signature.SignedAt ?? DateTimeOffset.UtcNow,
                    signatureDataUrl,
                    logoUrl);

                var bytes = await _pdf.FromHtmlAsync(html, $"Vertrag {contract.ContractNo} - signiert", ct);

                return File(bytes, "application/pdf", $"{contract.ContractNo}-signed.pdf");
            }
            catch (Exception ex)
            {
                // The renderer's own words are for the log, not for the screen.
                var reference = Guid.NewGuid().ToString("n")[..8];
                _logger.LogError(ex,
                    "Signed contract PDF failed. ContractId={ContractId} Reference={Reference}",
                    Id, reference);

                return SignedPdfUnavailable(
                    $"The signed PDF could not be produced. Reference {reference}.");
            }
        }

        /// <summary>
        /// Says why there is no signed PDF, in the way the caller can hear it.
        ///
        /// The button is an ordinary link, so the browser is here expecting a
        /// file or a page. Sending back neither — a bare status code — leaves it
        /// showing an error screen with our reason nowhere on it, so the reason
        /// comes back on the page the reader came from.
        /// </summary>
        private IActionResult SignedPdfUnavailable(string message)
        {
            TempData["Toast.Type"] = "error";
            TempData["Toast.Title"] = "Signed PDF unavailable";
            TempData["Toast.Message"] = message;

            return RedirectToPage("./Details", new { id = Id });
        }

        /// <summary>
        /// The signature that signed this contract — the most recent one that
        /// actually completed. A contract can carry several rows: a signing link
        /// that was issued and never used leaves one behind with no
        /// <c>SignedAt</c>, and reissuing the link leaves another.
        /// </summary>
        private static ContractSignature? SignedSignature(Contract contract) =>
            contract.Signatures
                .Where(s => s.SignedAt is not null)
                .OrderByDescending(s => s.SignedAt)
                .FirstOrDefault();

        /// <summary>
        /// The drawn signature as a data URL, or null when the row does not
        /// carry one. Without it there is nothing to put above the signature
        /// line, and a contract PDF headed "signed" with an empty line under it
        /// is worse than no download at all.
        /// </summary>
        private static string? SignatureImageOf(ContractSignature? signature)
        {
            if (signature?.SignatureData is null) return null;

            if (!signature.SignatureData.RootElement.TryGetProperty("dataUrl", out var dataUrl) ||
                dataUrl.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            var value = dataUrl.GetString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
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

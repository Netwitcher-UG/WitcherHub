using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    /// <summary>
    /// The contract position builder.
    ///
    /// Positions may come from the Service Catalog or be typed in by hand; a
    /// contract is valid with no catalog service at all as long as one valid manual
    /// position exists. Every total shown here is recalculated on the server — the
    /// figures the browser sends are treated as input, never as results.
    /// </summary>
    [Authorize]
    public class PositionsModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IContractPositions _positions;
        private readonly IServiceCatalog _services;
        private readonly IAiPositionOrganizer _organizer;
        private readonly IContractDraftService _drafts;
        private readonly IValidator<ManualPositionDto> _validator;
        private readonly ILogger<PositionsModel> _logger;

        public PositionsModel(
            IContract contracts,
            IContractPositions positions,
            IServiceCatalog services,
            IAiPositionOrganizer organizer,
            IContractDraftService drafts,
            IValidator<ManualPositionDto> validator,
            ILogger<PositionsModel> logger)
        {
            _contracts = contracts;
            _positions = positions;
            _services = services;
            _organizer = organizer;
            _drafts = drafts;
            _validator = validator;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ContractId { get; set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }
        public IReadOnlyList<ManualPositionDto> Positions { get; private set; } = Array.Empty<ManualPositionDto>();
        public PositionTotalsDto Totals { get; private set; } = new(0, 0, 0, 0, 0, "EUR");
        public IReadOnlyList<ContractDraftSummary> Drafts { get; private set; } = Array.Empty<ContractDraftSummary>();
        public List<CatalogOption> CatalogServices { get; private set; } = new();

        public sealed record CatalogOption(Guid Id, string Name, decimal BasePrice, string? Unit, string? Description);

        // ---- contract basics, previously on the retired Items/Manage page -----
        [BindProperty] public DocumentStatus BasicsStatus { get; set; }
        [BindProperty] public DateOnly? BasicsStartDate { get; set; }
        [BindProperty] public DateOnly? BasicsEndDate { get; set; }
        [BindProperty] public InvoiceSendMode BasicsInvoiceSendMode { get; set; }

        /// <summary>
        /// Free text that goes into the contract ahead of the positions. This was
        /// an unlabelled textarea above a button called "Save header"; it is the
        /// contract's introduction and is now named as such.
        /// </summary>
        [BindProperty] public string? BasicsIntroduction { get; set; }

        /// <summary>
        /// True once the contract is signed, when the terms must not move.
        /// </summary>
        public bool IsLocked => Contract?.Status is DocumentStatus.Signed or DocumentStatus.Terminated;

        /// <summary>
        /// A contract may proceed on positions, or on accepted contract text, or
        /// both. It does not require a Service Catalog entry.
        /// </summary>
        public bool HasPositions => Positions.Count > 0;
        public bool HasContractText => Drafts.Count > 0;
        public bool CanGenerate => HasPositions || HasContractText;

        /// <summary>Which step the builder opens on, from what is already filled in.</summary>
        public int CurrentStep =>
            !CanGenerate ? 2
            : !HasContractText ? 3
            : 4;

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ContractId == Guid.Empty) return NotFound();

            if (!await LoadAsync(ct)) return NotFound();

            BasicsStatus = Contract!.Status;
            BasicsStartDate = Contract.StartDate;
            BasicsEndDate = Contract.EndDate;
            BasicsInvoiceSendMode = Contract.InvoiceSendMode;
            BasicsIntroduction = Contract.Terms;

            return Page();
        }

        private async Task<bool> LoadAsync(CancellationToken ct)
        {
            Contract = await _contracts.GetContractAsync(ContractId, ct);
            if (Contract is null) return false;

            Positions = await _positions.GetPositionsAsync(ContractId, ct);
            Totals = _positions.CalculateTotals(Positions, Contract.Currency ?? "EUR");
            Drafts = await _drafts.GetDraftsAsync(ContractId, ct);

            var catalog = await _services.GetServicesAsync(1, 500, null, ct);
            CatalogServices = catalog.Items
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new CatalogOption(s.Id, s.Name ?? "", s.BasePrice, null, s.ServiceType.ToString()))
                .ToList();

            return true;
        }

        // ------------------------------------------------------------------
        // Contract basics
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostBasicsAsync(CancellationToken ct)
        {
            if (ContractId == Guid.Empty) return NotFound();

            try
            {
                await _contracts.UpdateHeaderAsync(
                    ContractId,
                    BasicsStatus,
                    BasicsStartDate,
                    BasicsEndDate,
                    BasicsIntroduction,
                    BasicsInvoiceSendMode,
                    ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Contract updated";
                TempData["Toast.Message"] = "The contract details were saved.";
            }
            catch (AppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Could not save";
                TempData["Toast.Message"] = ex.Message;
            }

            return RedirectToPage(new { contractId = ContractId });
        }

        // ------------------------------------------------------------------
        // Contract text pasted or written by hand
        //
        // A contract can be built entirely from text the customer or a lawyer
        // supplied, with no positions at all. It is stored as a draft version so
        // it sits alongside generated wording, can be compared against later
        // versions, and goes through the same approval gate.
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostImportTextAsync(
            [FromBody] ImportTextRequest? request, CancellationToken ct)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Text))
                return BadRequestJson("Paste the contract text first.");

            // Kept exactly as supplied. Nothing rewrites it, and the user chooses
            // separately whether to have it improved.
            var result = await _drafts.ImportTextAsync(ContractId, request.Text, "pasted", ct);

            if (!result.Succeeded)
                return new JsonResult(new { ok = false, message = result.FailureReason });

            return new JsonResult(new
            {
                ok = true,
                draft = result.Draft,
                message = $"Contract text stored as version {result.Draft!.Version}."
            });
        }

        public sealed class ImportTextRequest
        {
            public string? Text { get; set; }
        }

        // ------------------------------------------------------------------
        // Live totals — the browser proposes, the server decides.
        // ------------------------------------------------------------------

        public IActionResult OnPostTotals([FromBody] List<ManualPositionDto>? positions)
        {
            positions ??= new List<ManualPositionDto>();
            var totals = _positions.CalculateTotals(positions);

            return new JsonResult(new
            {
                ok = true,
                totals,
                lines = positions.Select(p => new { p.ClientId, net = p.NetTotal, vat = p.VatAmount, gross = p.GrossTotal })
            });
        }

        // ------------------------------------------------------------------
        // Save
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostSaveAsync(
            [FromBody] List<ManualPositionDto>? positions, CancellationToken ct)
        {
            positions ??= new List<ManualPositionDto>();

            if (positions.Count == 0)
                return BadRequestJson("Add at least one position before saving.");

            var errors = await ValidateAllAsync(positions, ct);
            if (errors.Count > 0)
                return new JsonResult(new { ok = false, message = "Some positions need attention.", errors });

            try
            {
                var totals = await _positions.SavePositionsAsync(ContractId, positions, ct);
                return new JsonResult(new { ok = true, totals, message = "Positions saved." });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                return BadRequestJson(ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // AI organizer — proposes, never applies.
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostOrganizeAsync(
            [FromBody] OrganizeRequest? request, CancellationToken ct)
        {
            if (request is null)
                return BadRequestJson("Nothing to organize.");

            var result = await _organizer.OrganizeAsync(new OrganizePositionsRequest
            {
                RoughInput = request.RoughInput ?? "",
                ExistingPositions = request.Positions ?? new List<ManualPositionDto>(),
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "EUR" : request.Currency
            }, ct);

            if (!result.Succeeded)
            {
                // The caller keeps whatever the user had; nothing is replaced.
                return new JsonResult(new
                {
                    ok = false,
                    transient = result.IsTransientFailure,
                    message = result.FailureReason
                });
            }

            // Returned for review only. The user applies it explicitly.
            return new JsonResult(new
            {
                ok = true,
                positions = result.Positions,
                totals = _positions.CalculateTotals(result.Positions),
                changes = result.Changes.Select(c => new { c.PositionTitle, c.Field, c.Before, c.After, kind = c.Kind.ToString() }),
                rejected = result.RejectedChanges.Select(c => new { c.PositionTitle, c.Field, c.Before, c.After }),
                model = result.Model
            });
        }

        public sealed class OrganizeRequest
        {
            public string? RoughInput { get; set; }
            public string? Currency { get; set; }
            public List<ManualPositionDto>? Positions { get; set; }
        }

        // ------------------------------------------------------------------
        // Draft generation and approval
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostGenerateDraftAsync(
            [FromBody] GenerateRequest? request, CancellationToken ct)
        {
            var result = await _drafts.GenerateAsync(ContractId, new GenerateDraftOptions
            {
                AdditionalInstructions = request?.AdditionalInstructions,
                OverwriteApproved = request?.OverwriteApproved ?? false
            }, ct);

            if (!result.Succeeded)
            {
                return new JsonResult(new
                {
                    ok = false,
                    transient = result.IsTransientFailure,
                    needsConfirmation = result.RequiresOverwriteConfirmation,
                    message = result.FailureReason
                });
            }

            return new JsonResult(new { ok = true, draft = result.Draft, message = $"Draft v{result.Draft!.Version} generated." });
        }

        public sealed class GenerateRequest
        {
            public string? AdditionalInstructions { get; set; }
            public bool OverwriteApproved { get; set; }
        }

        public async Task<IActionResult> OnPostSaveDraftAsync(
            [FromBody] SaveDraftRequest? request, CancellationToken ct)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.DocumentMarkdown))
                return BadRequestJson("The contract text cannot be empty.");

            var result = await _drafts.SaveEditedAsync(ContractId, request.Version, request.DocumentMarkdown, ct);

            return result.Succeeded
                ? new JsonResult(new { ok = true, draft = result.Draft, message = "Draft saved." })
                : new JsonResult(new { ok = false, message = result.FailureReason });
        }

        public sealed class SaveDraftRequest
        {
            public int Version { get; set; }
            public string DocumentMarkdown { get; set; } = "";
        }

        public async Task<IActionResult> OnPostApproveDraftAsync(
            [FromBody] ApproveRequest? request, CancellationToken ct)
        {
            if (request is null) return BadRequestJson("No version given.");

            var result = await _drafts.ApproveAsync(ContractId, request.Version, null, ct);

            return result.Succeeded
                ? new JsonResult(new { ok = true, draft = result.Draft, message = $"Version {request.Version} approved." })
                : new JsonResult(new { ok = false, message = result.FailureReason });
        }

        public sealed class ApproveRequest
        {
            public int Version { get; set; }
        }

        // ------------------------------------------------------------------

        private async Task<List<object>> ValidateAllAsync(List<ManualPositionDto> positions, CancellationToken ct)
        {
            var errors = new List<object>();
            var index = 0;

            foreach (var position in positions)
            {
                position.Position = ++index;

                var result = await _validator.ValidateAsync(position, ct);
                if (result.IsValid) continue;

                errors.Add(new
                {
                    clientId = position.ClientId,
                    position = position.Position,
                    title = position.Title,
                    messages = result.Errors.Select(e => e.ErrorMessage).ToList()
                });
            }

            return errors;
        }

        private JsonResult BadRequestJson(string message)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { ok = false, message });
        }
    }
}

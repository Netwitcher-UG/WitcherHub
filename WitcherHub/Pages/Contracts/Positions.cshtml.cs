using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Domain.Contracts;
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
        /// What this contract is built from. The rule lives in the domain and is
        /// the same one the server enforces, so the button the user sees and the
        /// answer they get from pressing it cannot disagree.
        /// </summary>
        public ContractSource Source { get; private set; }

        public bool HasPositions => Source.HasPositions;
        public bool HasContractText => Source.HasSuppliedText;
        public bool CanGenerate => Source.CanGenerate;

        /// <summary>The supplied source document, when there is one.</summary>
        public ContractDraftSummary? SuppliedSource =>
            Drafts.Where(d => d.IsSupplied).OrderByDescending(d => d.Version).FirstOrDefault();

        public ContractDraftSummary? ApprovedVersion => Drafts.FirstOrDefault(d => d.IsApproved);

        /// <summary>What analysis has found so far, for the review panel.</summary>
        public ContractExtractionDto? Extraction { get; private set; }

        /// <summary>
        /// What the financial engine made of that reading: committed money kept
        /// apart from estimated, variable and optional, and every amount it would
        /// not total listed with the reason.
        ///
        /// Null when this version predates the semantic pipeline or was never
        /// analysed — which the page shows as unknown, never as zero.
        /// </summary>
        public WitcherHub.Domain.Commercial.ContractFinancials? Financials { get; private set; }

        /// <summary>
        /// The contract-level figures. A supplied contract's money lives here,
        /// not on positions, and a null total means none was agreed rather than
        /// a total of zero.
        /// </summary>
        public ContractMoneyDto Money { get; private set; } = new(null, null, "EUR", false);

        public ContractSourceState SourceState { get; private set; }
        public ContractReviewState ReviewState { get; private set; }
        public ContractPreparationState PreparationState { get; private set; }

        /// <summary>
        /// The one action that makes sense next, from where the contract stands.
        /// Offering every action at all times is what made it unclear which
        /// version was the source, which the analysis, and which was ready to sign.
        /// </summary>
        public string NextStepHint => (SourceState, ReviewState, PreparationState) switch
        {
            (ContractSourceState.None, _, _) when !HasPositions =>
                "Add a position or paste the contract text to begin.",
            (ContractSourceState.AnalysisFailed, _, _) =>
                "Analysis failed. Retry it, edit the text, or carry on with the original wording — none of them is required.",
            (ContractSourceState.SuppliedTextSaved, _, _) =>
                "Analyse the supplied contract to read its values, or prepare it straight away.",
            (ContractSourceState.Analysed, ContractReviewState.RequiresReview, _) =>
                "Review the extracted values and confirm the ones you agree with.",
            (_, ContractReviewState.PartiallyConfirmed, _) =>
                "Some values are confirmed. Confirm the rest, or prepare the contract with what you have.",
            (_, _, ContractPreparationState.PreparedDraft) when ApprovedVersion is null =>
                "Review the prepared draft and approve it.",
            (_, _, ContractPreparationState.PreparedDraft) =>
                "A version is approved. Preview the PDF or send it for signature.",
            (_, _, ContractPreparationState.PreparationFailed) =>
                "Preparation failed. Your source text and confirmed values are unchanged.",
            _ => "Prepare the contract when you are ready."
        };

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
            Source = await _drafts.GetSourceAsync(ContractId, ct);

            var state = await _drafts.GetStateAsync(ContractId, ct);
            SourceState = state.SourceState;
            ReviewState = state.ReviewState;
            PreparationState = state.PreparationState;
            Money = state.Money;

            if (SuppliedSource is not null)
            {
                Extraction = await _drafts.GetExtractionAsync(ContractId, SuppliedSource.Version, ct);
                Financials = await _drafts.GetFinancialsAsync(ContractId, SuppliedSource.Version, ct);
            }

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
        // Analysis of supplied text
        //
        // Optional throughout. The contract can be prepared, approved and signed
        // from supplied text that was never analysed — analysis only offers to
        // read values out of it for review.
        // ------------------------------------------------------------------

        public async Task<IActionResult> OnPostAnalyzeAsync(
            [FromBody] VersionRequest? request, CancellationToken ct)
        {
            if (request is null) return BadRequestJson("No version given.");

            var result = await _drafts.AnalyzeAsync(ContractId, request.Version, ct);

            if (!result.Succeeded)
            {
                return new JsonResult(new
                {
                    ok = false,
                    transient = result.IsTransientFailure,
                    reference = result.CorrelationId,
                    message = result.FailureReason
                });
            }

            return new JsonResult(new
            {
                ok = true,
                extraction = result.Extraction,
                message = "The contract was analysed. Review the values before confirming them."
            });
        }

        public async Task<IActionResult> OnPostConfirmExtractionAsync(
            [FromBody] ConfirmExtractionRequest? request, CancellationToken ct)
        {
            if (request?.Extraction is null)
                return BadRequestJson("There is nothing to confirm.");

            var result = await _drafts.ConfirmExtractionAsync(
                ContractId, request.Version, request.Extraction, ct);

            if (!result.Succeeded)
                return new JsonResult(new { ok = false, message = result.FailureReason });

            // Read back from the database rather than echoing what was sent, so
            // the message and the screen both describe what is actually stored.
            // The old message said "Positions saved" — which was neither what this
            // action does nor evidence that anything had been written.
            var persisted = await _drafts.GetExtractionAsync(ContractId, request.Version, ct);

            var message = result.ConfirmedFieldCount == 0
                ? "No values were confirmed. Tick the values you agree with, then save."
                : $"Confirmed contract values saved ({result.ConfirmedFieldCount} of {result.StatedFieldCount} stated values).";

            return new JsonResult(new
            {
                ok = true,
                message,
                confirmedCount = result.ConfirmedFieldCount,
                statedCount = result.StatedFieldCount,
                extraction = persisted,
                money = result.Money
            });
        }

        public sealed class VersionRequest
        {
            public int Version { get; set; }
        }

        public sealed class ConfirmExtractionRequest
        {
            public int Version { get; set; }
            public ContractExtractionDto? Extraction { get; set; }
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

            // Saving zero positions is allowed. It is how a contract built from
            // supplied text is saved, and how positions are cleared from one that
            // no longer needs them. Whether the contract has enough behind it to
            // be generated is a different question, answered by ContractSource
            // when generation is attempted.
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
                IdempotencyKey = request?.IdempotencyKey
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

            // The message names what was produced and where it stands, because
            // "Draft generated" left the user counting versions to work out which
            // one was the original, which the analysis, and which was ready to sign.
            var draft = result.Draft!;

            var message = result.WasAlreadyPrepared
                ? $"{draft.KindLabel} version {draft.Version} was already created."
                : $"{draft.KindLabel} version {draft.Version} created as a draft.";

            return new JsonResult(new
            {
                ok = true,
                draft,
                version = draft.Version,
                draftId = draft.Id,
                message
            });
        }

        public sealed class GenerateRequest
        {
            public string? AdditionalInstructions { get; set; }

            /// <summary>
            /// Sent by the browser so a double click, or a retry after a timeout,
            /// returns the version the first request produced rather than adding
            /// another one.
            /// </summary>
            public string? IdempotencyKey { get; set; }
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

            var result = await _drafts.ApproveAsync(
                ContractId, request.Version, null, request.ConfirmReplacingApproved, ct);

            if (!result.Succeeded)
            {
                return new JsonResult(new
                {
                    ok = false,
                    needsConfirmation = result.RequiresOverwriteConfirmation,
                    message = result.FailureReason
                });
            }

            var superseded = await _drafts.GetDraftsAsync(ContractId, ct);
            var previous = superseded
                .Where(d => d.Status == ContractDraftStatus.Superseded)
                .OrderByDescending(d => d.Version)
                .FirstOrDefault();

            var message = previous is null
                ? $"Version {request.Version} approved."
                : $"Version {request.Version} approved. Version {previous.Version} remains in the history.";

            return new JsonResult(new { ok = true, draft = result.Draft, message });
        }

        public sealed class ApproveRequest
        {
            public int Version { get; set; }

            /// <summary>
            /// Required only when another version is already approved. Approval is
            /// where replacing the active version is actually decided.
            /// </summary>
            public bool ConfirmReplacingApproved { get; set; }
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

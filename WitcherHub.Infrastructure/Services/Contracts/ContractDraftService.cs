using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Domain.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Produces contract wording from confirmed positions, one version at a time.
    ///
    /// The model writes prose. It is given the commercial figures as fixed facts to
    /// restate, and the figures stored on the contract are never taken back from
    /// its answer — the positions remain the source of truth for what was agreed.
    /// </summary>
    public sealed class ContractDraftService : IContractDraftService
    {
        public const string CurrentPromptVersion = "contract-draft-v1";

        private readonly AppDbContext _db;
        private readonly IContractPositions _positions;
        private readonly IAiTextGenerator _ai;
        private readonly IContractTextAnalyzer _analyzer;
        private readonly OpenAIOptions _openAi;
        private readonly ContractTemplateOptions _template;
        private readonly ILogger<ContractDraftService> _logger;

        public ContractDraftService(
            AppDbContext db,
            IContractPositions positions,
            IAiTextGenerator ai,
            IContractTextAnalyzer analyzer,
            IOptions<OpenAIOptions> openAi,
            IOptions<ContractTemplateOptions> template,
            ILogger<ContractDraftService> logger)
        {
            _db = db;
            _positions = positions;
            _ai = ai;
            _analyzer = analyzer;
            _openAi = openAi.Value;
            _template = template.Value;
            _logger = logger;
        }

        public async Task<ContractSource> GetSourceAsync(Guid contractId, CancellationToken ct = default)
        {
            var positionCount = await _db.Set<ContractItem>()
                .CountAsync(i => i.ContractId == contractId, ct);

            var drafts = await _db.Set<ContractDraft>()
                .Where(d => d.ContractId == contractId)
                .Select(d => new { d.IsApproved })
                .ToListAsync(ct);

            return ContractSource.From(
                positionCount,
                hasSuppliedText: drafts.Count > 0,
                hasApprovedText: drafts.Any(d => d.IsApproved));
        }

        public async Task<ContractWorkflowState> GetStateAsync(
            Guid contractId, CancellationToken ct = default)
        {
            var contract = await _db.Contracts
                .AsNoTracking()
                .Where(c => c.Id == contractId)
                .Select(c => new
                {
                    c.SourceState,
                    c.ReviewState,
                    c.PreparationState,
                    c.AgreedTotalNet,
                    c.AgreedTotalVatRatePercent,
                    c.Currency,
                    c.PriceDeliberatelyUnspecified
                })
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundAppException("Contract not found.");

            return new ContractWorkflowState(
                contract.SourceState,
                contract.ReviewState,
                contract.PreparationState,
                new ContractMoneyDto(
                    contract.AgreedTotalNet,
                    contract.AgreedTotalVatRatePercent,
                    contract.Currency,
                    contract.PriceDeliberatelyUnspecified));
        }

        public async Task<ContractDraftResult> GenerateAsync(
            Guid contractId, GenerateDraftOptions options, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var contract = await LoadAsync(contractId, ct);

            // Producing a version never touches an approved one.
            //
            // This used to refuse outright and ask the user to confirm replacing
            // the approved wording — before the new draft even existed, and on
            // an operation that only ever appends. Versions accumulate: a new one
            // is created as a Draft, the approved version stays approved and
            // stays in the history, and the question of which is active belongs
            // to approval, where it is actually decided.
            var positions = await _positions.GetPositionsAsync(contractId, ct);

            // The rule, from the one place it lives. This used to be
            // "positions.Count == 0 → refuse", which is why a contract whose
            // wording was a document the customer supplied could never be
            // generated: it has no positions and never will.
            var source = ContractSource.From(
                positions.Count,
                hasSuppliedText: contract.Drafts.Count > 0,
                hasApprovedText: contract.Drafts.Any(d => d.IsApproved));

            if (!source.CanGenerate)
                return ContractDraftResult.Failed(source.BlockingReason!);

            // Remember what it is actually built from, rather than working it out
            // again from a row count every time somebody asks.
            contract.SourceMode = source.Mode;

            var totals = _positions.CalculateTotals(positions, contract.Currency);

            // With supplied text, the document is the contract. Preparing it means
            // putting the current parties into it — deterministically, with no
            // model involved — so a contract can be produced, approved and signed
            // even when the assistant is unreachable.
            if (source.Mode is ContractSourceMode.SuppliedText)
                return await PrepareSuppliedAsync(contract, options, ct);

            string document;
            try
            {
                document = await _ai.GenerateTextAsync(BuildPrompt(contract, positions, totals, options));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                // Everything the user entered is already saved; only the wording
                // step failed. The message now says which failure it was and
                // carries the reference that finds it in the log.
                return ContractDraftResult.Failed(
                    ex.UserMessage + " Your positions and contract text are saved — you can write or paste the " +
                    "wording by hand, or try again.",
                    ex.IsTransient);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract draft generation failed for {ContractId}.", contractId);

                return ContractDraftResult.Failed(
                    "The assistant could not produce a draft. Your positions are saved — you can write the contract text by hand and save it.",
                    transient: true);
            }

            if (string.IsNullOrWhiteSpace(document))
            {
                return ContractDraftResult.Failed(
                    "The assistant returned an empty document. Your positions are saved.",
                    transient: true);
            }

            // A hybrid contract keeps the supplied wording and appends the service
            // schedule generated from the positions, rather than replacing one
            // with the other: both were supplied on purpose.
            if (source.Mode is ContractSourceMode.Hybrid)
            {
                var supplied = LatestSupplied(contract);

                if (supplied is not null)
                {
                    document =
                        supplied.DocumentMarkdown.TrimEnd() +
                        "\n\n---\n\n" +
                        document.Trim();
                }
            }

            var draft = new ContractDraft
            {
                ContractId = contractId,
                Version = await NextVersionAsync(contractId, ct),
                DocumentMarkdown = document.Trim(),
                PositionsSnapshot = JsonSerializer.SerializeToDocument(positions),
                PromptVersion = CurrentPromptVersion,
                TemplateVersion = _template.BaseDePath,
                Model = _openAi.Model,
                GeneratedBy = "openai",
                GeneratedAt = DateTimeOffset.UtcNow,
                Kind = ContractDraftKind.Generated,
                SourceDraftId = source.Mode is ContractSourceMode.Hybrid ? LatestSupplied(contract)?.Id : null
            };

            _db.Set<ContractDraft>().Add(draft);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Generated draft v{Version} for contract {ContractId} from {Mode} using {Model}.",
                draft.Version, contractId, source.Mode, _openAi.Model);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, totals) };
        }

        /// <summary>
        /// Produces the customer-specific version of a supplied contract.
        ///
        /// Deterministic on purpose. The source document is the agreement; all
        /// that is needed is to put the current parties into it, and doing that
        /// with string replacement rather than a model means it cannot reword a
        /// clause, and it still works when the assistant is down. The original is
        /// never touched: this writes a new version that points back at it.
        /// </summary>
        private async Task<ContractDraftResult> PrepareSuppliedAsync(
            Contract contract, GenerateDraftOptions options, CancellationToken ct)
        {
            var supplied = LatestSupplied(contract);

            if (supplied is null)
                return ContractDraftResult.Failed("There is no supplied contract text to prepare.");

            // A repeat of a request already carried out returns what it produced
            // the first time. Two clicks on one button are one intention, and
            // without this they were two versions.
            if (!string.IsNullOrWhiteSpace(options.IdempotencyKey) &&
                string.Equals(contract.LastPreparationKey, options.IdempotencyKey, StringComparison.Ordinal) &&
                contract.LastPreparedDraftId is { } alreadyMade)
            {
                var existing = contract.Drafts.FirstOrDefault(d => d.Id == alreadyMade);

                if (existing is not null)
                {
                    _logger.LogInformation(
                        "Preparation request {Key} for contract {ContractId} was already carried out as v{Version}.",
                        options.IdempotencyKey, contract.Id, existing.Version);

                    return new ContractDraftResult
                    {
                        Succeeded = true,
                        Draft = ToSummary(existing, null),
                        WasAlreadyPrepared = true
                    };
                }
            }

            var parties = await BuildPartyDetailsAsync(contract, ct);
            var confirmed = options.ConfirmedReplacements
                .Select(r => new PartyReplacement(r.Field, r.OldValue, r.NewValue, IsPlaceholder: false))
                .ToList();

            var merge = ContractPartyMerge.Merge(supplied.DocumentMarkdown, parties, confirmed);

            if (merge.MissingFields.Count > 0 && merge.Applied.Count == 0)
            {
                // Only blocking when there was a placeholder to fill and nothing to
                // fill it with; a document that already names the parties needs
                // nothing from us.
                if (ContainsPlaceholder(supplied.DocumentMarkdown))
                {
                    contract.PreparationState = ContractPreparationState.PreparationFailed;
                    await _db.SaveChangesAsync(ct);

                    return ContractDraftResult.Failed(
                        "The contract has placeholders to fill in, but this information is missing: " +
                        string.Join(", ", merge.MissingFields) +
                        ". Add it to the company settings or the customer record and prepare the contract again.");
                }
            }

            // The confirmed values are folded in as a terms summary, so a prepared
            // draft is more than a copy of the pasted text: it carries what a
            // person actually agreed, next to the wording it was read from.
            var confirmedTerms = await BuildConfirmedTermsBlockAsync(contract, supplied, ct);

            var document = merge.Document.TrimEnd();

            if (!string.IsNullOrWhiteSpace(confirmedTerms))
                document = document + "\n\n---\n\n" + confirmedTerms;

            var draft = new ContractDraft
            {
                ContractId = contract.Id,
                Version = await NextVersionAsync(contract.Id, ct),
                DocumentMarkdown = document.Trim(),
                PromptVersion = null,
                TemplateVersion = null,
                Model = null,

                // No model was involved, and saying so is what lets a later reader
                // tell a merged supplied document from generated wording.
                GeneratedBy = "prepared-from-supplied",
                GeneratedAt = DateTimeOffset.UtcNow,
                Kind = ContractDraftKind.Prepared,
                Status = ContractDraftStatus.Draft,
                SourceDraftId = supplied.Id,
                SourceLanguage = supplied.SourceLanguage
            };

            _db.Set<ContractDraft>().Add(draft);

            contract.PreparationState = ContractPreparationState.PreparedDraft;
            contract.LastPreparationKey = options.IdempotencyKey;
            contract.LastPreparedDraftId = draft.Id;

            contract.PartySnapshot = JsonSerializer.SerializeToDocument(new
            {
                companyName = parties.CompanyName,
                companyAddress = parties.CompanyAddress,
                customerName = parties.CustomerName,
                customerAddress = parties.CustomerAddress,
                takenAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Prepared supplied contract v{Version} for contract {ContractId} from source v{Source}. " +
                "{Applied} placeholder(s) filled, {Missing} field(s) missing.",
                draft.Version, contract.Id, supplied.Version, merge.Applied.Count, merge.MissingFields.Count);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        /// <summary>
        /// The most recent document the user supplied. Ordered by version alone:
        /// approving a prepared draft does not make it the source, and the source
        /// is the thing every prepared version is prepared from.
        /// </summary>
        private static ContractDraft? LatestSupplied(Contract contract) =>
            contract.Drafts
                .Where(d => d.Kind == ContractDraftKind.Supplied || d.GeneratedBy == "pasted")
                .OrderByDescending(d => d.Version)
                .FirstOrDefault()
            ?? contract.Drafts.OrderBy(d => d.Version).FirstOrDefault();

        /// <summary>
        /// The confirmed commercial facts, written out for the prepared document.
        ///
        /// Only values a person ticked appear. An unconfirmed reading is a
        /// reading, and putting it into the contract would be asserting a term
        /// nobody agreed to.
        /// </summary>
        private async Task<string?> BuildConfirmedTermsBlockAsync(
            Contract contract, ContractDraft supplied, CancellationToken ct)
        {
            var extraction = await GetExtractionAsync(contract.Id, supplied.Version, ct);
            if (extraction is null) return null;

            var lines = new List<string>();

            void Add(string label, ExtractedValue value)
            {
                if (value.Confirmed && value.HasValue)
                    lines.Add($"- **{label}:** {value.Value!.Trim()}");
            }

            Add("Vertragsart", extraction.ContractType);
            Add("Laufzeitbeginn", extraction.StartDate);
            Add("Laufzeitende", extraction.EndDate);
            Add("Laufzeit", extraction.Duration);
            Add("Verlängerung", extraction.RenewalRules);
            Add("Kündigungsfrist", extraction.TerminationNotice);
            Add("Gesamtpreis", extraction.TotalPrice);
            Add("Währung", extraction.Currency);
            Add("Umsatzsteuer", extraction.VatRate);
            Add("Steuerliche Behandlung", extraction.VatTreatment);
            Add("Rabatte", extraction.Discounts);
            Add("Abrechnungszyklus", extraction.BillingCycle);
            Add("Zahlungsplan", extraction.PaymentSchedule);
            Add("Fälligkeit", extraction.PaymentDueDates);
            Add("Anzahlung", extraction.Deposit);
            Add("Wiederkehrende Beträge", extraction.RecurringCharges);

            if (lines.Count == 0) return null;

            return "## Bestätigte Vertragsdaten\n\n" + string.Join("\n", lines);
        }

        private static bool ContainsPlaceholder(string document) =>
            document.Contains("[COMPANY", StringComparison.OrdinalIgnoreCase) ||
            document.Contains("[CUSTOMER", StringComparison.OrdinalIgnoreCase) ||
            document.Contains("[CLIENT", StringComparison.OrdinalIgnoreCase) ||
            document.Contains("[CONTRACT_DATE", StringComparison.OrdinalIgnoreCase);

        private async Task<PartyDetails> BuildPartyDetailsAsync(Contract contract, CancellationToken ct)
        {
            var customer = await _db.Contracts
                .Where(c => c.Id == contract.Id)
                .Select(c => new
                {
                    c.Project.Customer.Name,
                    Address = c.Project.Customer.Addresses
                        .OrderByDescending(a => a.IsDefault)
                        .Select(a => new { a.StreetRaw, a.AddressLine2, a.PostalCode, a.City, a.Country })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            var providerLines = (_template.ProviderBlock ?? "")
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var customerAddress = customer?.Address is null
                ? null
                : string.Join("\n", new[]
                {
                    customer.Address.StreetRaw,
                    customer.Address.AddressLine2,
                    $"{customer.Address.PostalCode} {customer.Address.City}".Trim(),
                    customer.Address.Country
                }.Where(l => !string.IsNullOrWhiteSpace(l)));

            return new PartyDetails(
                CompanyName: providerLines.FirstOrDefault(),
                CompanyAddress: providerLines.Length > 1 ? string.Join("\n", providerLines.Skip(1)) : null,
                CustomerName: customer?.Name,
                CustomerAddress: customerAddress,
                ContractDate: contract.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        }

        public async Task<IReadOnlyList<ContractDraftSummary>> GetDraftsAsync(
            Guid contractId, CancellationToken ct = default)
        {
            var drafts = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId)
                .OrderByDescending(d => d.Version)
                .ToListAsync(ct);

            return drafts.Select(d => ToSummary(d, null)).ToList();
        }

        public async Task<ContractDraftSummary?> GetDraftAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var draft = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct);

            return draft is null ? null : ToSummary(draft, null);
        }

        public async Task<ContractDraftResult> ImportTextAsync(
            Guid contractId, string documentText, string source, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentText))
                return ContractDraftResult.Failed("The contract text cannot be empty.");

            var contract = await LoadAsync(contractId, ct);

            var draft = new ContractDraft
            {
                Id = Guid.NewGuid(),
                ContractId = contractId,
                Version = await NextVersionAsync(contractId, ct),

                // Trimmed at the ends only. Paragraphs, headings, lists and line
                // breaks inside are the document's structure, and a contract that
                // comes back reflowed is not the document that was supplied.
                DocumentMarkdown = documentText.Trim(),

                // No prompt, no template, no model: this text was not generated.
                // Recording that plainly is what lets a later reader tell supplied
                // wording from wording the assistant produced.
                GeneratedBy = source,
                GeneratedAt = DateTimeOffset.UtcNow,
                Kind = ContractDraftKind.Supplied,
                Status = ContractDraftStatus.Draft,
                IsImmutableSource = true,
                SourceLanguage = DetectLanguage(documentText),
                ExtractionStatus = ContractExtractionStatus.NotAnalysed
            };

            _db.Add(draft);

            // A contract that arrives as a document is a supplied-text contract
            // from that moment, whether or not positions are ever added.
            contract.SourceMode = contract.Items.Count > 0
                ? ContractSourceMode.Hybrid
                : ContractSourceMode.SuppliedText;

            contract.SourceState = ContractSourceState.SuppliedTextSaved;
            contract.ReviewState = ContractReviewState.RequiresReview;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Contract {ContractId} received supplied text as version {Version} ({Source}).",
                contractId, draft.Version, source);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        public async Task<ContractDraftResult> SaveEditedAsync(
            Guid contractId, int version, string documentMarkdown, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentMarkdown))
                return ContractDraftResult.Failed("The contract text cannot be empty.");

            var draft = await _db.Set<ContractDraft>()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct)
                ?? throw new NotFoundAppException("Draft not found.");

            if (draft.IsApproved)
            {
                return ContractDraftResult.NeedsConfirmation(
                    "This version is approved and cannot be edited. Generate a new version instead.");
            }

            if (draft.IsImmutableSource)
            {
                // The supplied original is the evidence of what was handed over.
                // Editing it in place would destroy the only copy of that.
                return ContractDraftResult.NeedsConfirmation(
                    "This is the contract exactly as it was supplied and is kept unchanged. " +
                    "Prepare it to produce an editable version.");
            }

            draft.DocumentMarkdown = documentMarkdown.Trim();
            draft.GeneratedBy = "human-edit";
            draft.Kind = ContractDraftKind.HumanEdited;

            await _db.SaveChangesAsync(ct);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        public async Task<ContractDraftResult> ApproveAsync(
            Guid contractId,
            int version,
            Guid? approvedById,
            bool confirmReplacingApproved = false,
            CancellationToken ct = default)
        {
            var contract = await LoadAsync(contractId, ct);

            var draft = contract.Drafts.FirstOrDefault(d => d.Version == version)
                ?? throw new NotFoundAppException("Draft not found.");

            var previouslyApproved = contract.Drafts
                .Where(d => d.IsApproved && d.Version != version)
                .ToList();

            // Approving over an existing approval is the decision that changes
            // which text is active, so this is where it is confirmed. It used to
            // be asked on preparation, which only ever appends a version and
            // replaces nothing.
            if (previouslyApproved.Count > 0 && !confirmReplacingApproved)
            {
                var current = previouslyApproved.OrderByDescending(d => d.ApprovedAt).First();

                return ContractDraftResult.NeedsConfirmation(
                    $"Version {current.Version} is already approved. Approving version {version} will make it " +
                    $"the active version. Version {current.Version} stays in the history.");
            }

            // The previous approval is superseded, not erased. A superseded
            // version is still the text somebody may have signed, so it keeps its
            // approval date and its hash and merely stops being the active one.
            foreach (var other in previouslyApproved)
            {
                other.IsApproved = false;
                other.Status = ContractDraftStatus.Superseded;
                other.SupersededAt = DateTimeOffset.UtcNow;
            }

            draft.IsApproved = true;
            draft.Status = ContractDraftStatus.Approved;
            draft.SupersededAt = null;
            draft.ApprovedAt = DateTimeOffset.UtcNow;
            draft.ApprovedById = approvedById;
            draft.DocumentHash = Sha256(draft.DocumentMarkdown);

            // The approved wording becomes the contract's terms, which is what the
            // signing page and the PDF read from.
            contract.Terms = draft.DocumentMarkdown;
            contract.ApprovedDraftId = draft.Id;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Draft v{Version} approved for contract {ContractId}. Hash {Hash}. Superseded: {Superseded}.",
                version, contractId, draft.DocumentHash,
                previouslyApproved.Count == 0 ? "none" : string.Join(", ", previouslyApproved.Select(d => d.Version)));

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        // -------------------------------------------------------------------
        // Analysis of a supplied document
        //
        // Reading, not rewriting. Nothing here changes the stored document, and
        // nothing it finds becomes contract data until a person confirms it.
        // -------------------------------------------------------------------

        public async Task<ContractAnalysisResult> AnalyzeAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var contract = await LoadAsync(contractId, ct);

            var draft = contract.Drafts.FirstOrDefault(d => d.Version == version)
                ?? throw new NotFoundAppException("Draft not found.");

            var result = await _analyzer.AnalyzeAsync(draft.DocumentMarkdown, draft.SourceLanguage, ct);

            if (!result.Succeeded)
            {
                // A failed reading is recorded as a failed reading. The document
                // itself is untouched and remains the contract's source, so the
                // user can carry on with the original text.
                draft.ExtractionStatus = ContractExtractionStatus.Failed;
                contract.SourceState = ContractSourceState.AnalysisFailed;

                await _db.SaveChangesAsync(ct);
                return result;
            }

            draft.ExtractedTerms = JsonSerializer.SerializeToDocument(result.Extraction);
            draft.ExtractionStatus = ContractExtractionStatus.Analysed;
            draft.ExtractedAt = DateTimeOffset.UtcNow;

            if (result.Extraction!.Language.HasValue)
                draft.SourceLanguage = result.Extraction.Language.Value;

            contract.SourceState = ContractSourceState.Analysed;
            contract.ReviewState = ContractReviewState.RequiresReview;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Analysed supplied contract v{Version} of {ContractId}: {Positions} candidate position(s), " +
                "{Warnings} warning(s), price missing: {PriceMissing}.",
                version, contractId, result.Extraction.Positions.Count,
                result.Extraction.Warnings.Count, result.Extraction.PriceMissing);

            return result;
        }

        public async Task<ContractExtractionDto?> GetExtractionAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var stored = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId && d.Version == version)
                .Select(d => d.ExtractedTerms)
                .FirstOrDefaultAsync(ct);

            if (stored is null) return null;

            try
            {
                return stored.Deserialize<ContractExtractionDto>();
            }
            catch (JsonException ex)
            {
                // Stored extraction from an older shape. Not worth failing a page
                // over — the user can analyse again.
                _logger.LogWarning(ex, "Stored extraction for {ContractId} v{Version} could not be read.",
                    contractId, version);
                return null;
            }
        }

        public async Task<ContractDraftResult> ConfirmExtractionAsync(
            Guid contractId, int version, ContractExtractionDto confirmed, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(confirmed);

            var contract = await LoadAsync(contractId, ct);

            var draft = contract.Drafts.FirstOrDefault(d => d.Version == version)
                ?? throw new NotFoundAppException("Draft not found.");

            // Everything reviewed is stored, confirmed or not, together with which
            // fields were ticked. A field the user cleared or corrected has to
            // survive a reload, and the confirmation flags are the record of what
            // a person actually agreed to.
            draft.ExtractedTerms = JsonSerializer.SerializeToDocument(confirmed);
            draft.ExtractionConfirmedAt = DateTimeOffset.UtcNow;

            var confirmedCount = CountConfirmed(confirmed);
            var statedCount = CountStated(confirmed);

            draft.ExtractionStatus = confirmedCount > 0
                ? ContractExtractionStatus.Confirmed
                : ContractExtractionStatus.Analysed;

            contract.ReviewState =
                confirmedCount == 0 ? ContractReviewState.RequiresReview
                : confirmedCount >= statedCount ? ContractReviewState.Confirmed
                : ContractReviewState.PartiallyConfirmed;

            // Only confirmed values are promoted onto the contract. An unconfirmed
            // reading stays a reading: it is visible in the review screen and has
            // no effect on what the contract says it costs.
            if (confirmed.TotalPrice.Confirmed &&
                ContractTextAnalyzer.TryParseAmount(confirmed.TotalPrice.Value, out var total))
            {
                contract.AgreedTotalNet = total;
                contract.PriceDeliberatelyUnspecified = false;
            }

            if (confirmed.VatRate.Confirmed &&
                ContractTextAnalyzer.TryParseAmount(confirmed.VatRate.Value, out var vat))
            {
                contract.AgreedTotalVatRatePercent = vat;
            }

            if (confirmed.Currency.Confirmed && confirmed.Currency.HasValue)
                contract.Currency = NormaliseCurrency(confirmed.Currency.Value!) ?? contract.Currency;

            // Payment terms are the schedule, the due dates and the deposit taken
            // together; storing only one of the three left the contract claiming
            // terms it did not have.
            var paymentTerms = string.Join(" · ", new[]
                {
                    confirmed.PaymentSchedule.Confirmed ? confirmed.PaymentSchedule.Value : null,
                    confirmed.PaymentDueDates.Confirmed ? confirmed.PaymentDueDates.Value : null,
                    confirmed.Deposit.Confirmed ? confirmed.Deposit.Value : null,
                    confirmed.BillingCycle.Confirmed ? confirmed.BillingCycle.Value : null
                }
                .Where(v => !string.IsNullOrWhiteSpace(v)));

            if (paymentTerms.Length > 0)
                contract.PaymentTermsText = Truncate(paymentTerms, 2000);

            // A price that is deliberately absent is a decision, recorded as one.
            if (confirmed.PriceMissing && !confirmed.TotalPrice.HasValue)
            {
                contract.AgreedTotalNet = null;
                contract.PriceDeliberatelyUnspecified = confirmed.TotalPrice.Confirmed;
            }

            if (confirmed.StartDate.Confirmed && TryParseDate(confirmed.StartDate.Value, out var start))
                contract.StartDate = start;

            if (confirmed.EndDate.Confirmed && TryParseDate(confirmed.EndDate.Value, out var end))
                contract.EndDate = end;

            // The parties as confirmed, recorded next to the ones from our records
            // so a later reader can see which the contract was prepared against.
            contract.PartySnapshot = JsonSerializer.SerializeToDocument(new
            {
                companyName = Confirmed(confirmed.ProviderName),
                companyAddress = Confirmed(confirmed.ProviderAddress),
                companyRepresentative = Confirmed(confirmed.ProviderRepresentative),
                customerName = Confirmed(confirmed.CustomerName),
                customerAddress = Confirmed(confirmed.CustomerAddress),
                customerRepresentative = Confirmed(confirmed.CustomerRepresentative),
                sourceVersion = version,
                takenAt = DateTimeOffset.UtcNow
            });

            // One transaction. Nothing is reported as saved before this returns.
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Extracted terms confirmed for contract {ContractId} v{Version}: {Confirmed} of {Stated} " +
                "stated value(s) confirmed. Review state {ReviewState}.",
                contractId, version, confirmedCount, statedCount, contract.ReviewState);

            return new ContractDraftResult
            {
                Succeeded = true,
                Draft = ToSummary(draft, null),
                ConfirmedFieldCount = confirmedCount,
                StatedFieldCount = statedCount,
                Money = new ContractMoneyDto(
                    contract.AgreedTotalNet,
                    contract.AgreedTotalVatRatePercent,
                    contract.Currency,
                    contract.PriceDeliberatelyUnspecified)
            };
        }

        private static string? Confirmed(ExtractedValue value) =>
            value.Confirmed && value.HasValue ? value.Value : null;

        private static int CountConfirmed(ContractExtractionDto e) =>
            AllValuesOf(e).Count(v => v.Confirmed && v.HasValue);

        private static int CountStated(ContractExtractionDto e) =>
            AllValuesOf(e).Count(v => v.HasValue);

        private static IEnumerable<ExtractedValue> AllValuesOf(ContractExtractionDto e) =>
            typeof(ContractExtractionDto).GetProperties()
                .Where(p => p.PropertyType == typeof(ExtractedValue))
                .Select(p => p.GetValue(e) as ExtractedValue)
                .Where(v => v is not null)!;

        /// <summary>
        /// Currency has to be a three-letter code to be stored. "Euro", "EUR" and
        /// "€" all reach here from real documents, and writing "Euro" into a field
        /// the rest of the system formats as a code breaks every total after it.
        /// </summary>
        internal static string? NormaliseCurrency(string value)
        {
            var trimmed = value.Trim();

            var known = trimmed.ToUpperInvariant() switch
            {
                "EUR" or "EURO" or "€" => "EUR",
                "USD" or "US$" or "$" => "USD",
                "CHF" => "CHF",
                "GBP" or "£" => "GBP",
                _ => null
            };

            if (known is not null) return known;

            return trimmed.Length == 3 && trimmed.All(char.IsLetter)
                ? trimmed.ToUpperInvariant()
                : null;
        }

        /// <summary>
        /// Accepts what German contracts actually write. DateOnly.TryParse alone
        /// reads "01.08.2026" against the invariant culture and gets it wrong or
        /// refuses it.
        /// </summary>
        internal static bool TryParseDate(string? value, out DateOnly date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var text = value.Trim();

            string[] formats = { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "MM/dd/yyyy" };

            return DateOnly.TryParseExact(
                       text, formats, System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out date)
                || DateOnly.TryParse(
                       text, System.Globalization.CultureInfo.GetCultureInfo("de-DE"),
                       System.Globalization.DateTimeStyles.None, out date);
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];

        /// <summary>
        /// A cheap language guess from stopwords, used only to tell the analyser
        /// what to expect and to label the version. Never a commercial fact.
        /// </summary>
        internal static string? DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var sample = text.Length > 4000 ? text[..4000] : text;
            var words = sample.ToLowerInvariant().Split(
                new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries);

            var german = words.Count(w => w is "und" or "der" or "die" or "das" or "vertrag" or "nicht" or "wird" or "für");
            var english = words.Count(w => w is "and" or "the" or "shall" or "agreement" or "of" or "this");

            if (german == 0 && english == 0) return null;

            return german >= english ? "de" : "en";
        }

        // -------------------------------------------------------------------

        private async Task<Contract> LoadAsync(Guid contractId, CancellationToken ct)
        {
            if (contractId == Guid.Empty)
                throw new BadRequestAppException("Invalid contract id.");

            return await _db.Contracts
                .Include(c => c.Drafts)
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct)
                ?? throw new NotFoundAppException("Contract not found.");
        }

        private async Task<int> NextVersionAsync(Guid contractId, CancellationToken ct)
        {
            var highest = await _db.Set<ContractDraft>()
                .Where(d => d.ContractId == contractId)
                .Select(d => (int?)d.Version)
                .MaxAsync(ct) ?? 0;

            return highest + 1;
        }

        private static string BuildPrompt(
            Contract contract,
            IReadOnlyList<ManualPositionDto> positions,
            PositionTotalsDto totals,
            GenerateDraftOptions options)
        {
            // Deliberately excludes customer identity: the contract template fills
            // the parties in, and the model does not need personal data to write the
            // service sections.
            var payload = positions.Select(p => new
            {
                position = p.Position,
                title = p.Title,
                serviceType = p.ServiceType,
                description = p.Description,
                scope = p.Scope,
                deliverables = p.Deliverables,
                quantity = p.Quantity,
                unit = p.Unit,
                pricingModel = p.PricingModel.ToString(),
                unitPrice = p.UnitPrice,
                currency = p.Currency,
                vatRate = p.VatRate,
                discountType = p.DiscountType?.ToString(),
                discountValue = p.DiscountValue,
                netTotal = p.NetTotal,
                billingCycle = p.BillingCycle.ToString(),
                durationPeriods = p.DurationPeriods,
                deliveryMethod = p.DeliveryMethod,
                activationMethod = p.ActivationMethod.ToString(),
                startDate = p.StartDate?.ToString("yyyy-MM-dd"),
                deliveryDate = p.DeliveryDate?.ToString("yyyy-MM-dd"),
                acceptanceCriteria = p.AcceptanceCriteria,
                customerResponsibilities = p.CustomerResponsibilities,
                assumptions = p.Assumptions,
                exclusions = p.Exclusions,
                notes = p.Notes
            });

            return $$"""
                Write the service schedule of an agency contract, in {{options.Language}}, as Markdown.

                Produce these sections, in order:
                1. Gegenstand des Vertrags — what is being delivered, in prose.
                2. Leistungsumfang — one subsection per position, using its scope and deliverables.
                3. Mitwirkungspflichten des Kunden — customer responsibilities.
                4. Abnahme — acceptance criteria.
                5. Annahmen und Ausschlüsse — assumptions and exclusions.
                6. Vergütung und Zahlung — restate the figures below exactly.
                7. Laufzeit und Aktivierung — billing cycle, duration, activation.

                Rules:
                - Restate every number exactly as given. Do not recalculate, round or
                  convert anything, and do not add a figure that is not below.
                - Do not add legal clauses, liability terms, warranties or payment
                  obligations beyond the figures given. Separate legal sections are
                  appended from an approved template.
                - Do not invent services, dates or parties.
                - If information is missing, write "wird noch festgelegt" rather than
                  inventing a value.
                - Output only the contract Markdown. No commentary.

                Currency: {{totals.Currency}}
                Net total: {{totals.Subtotal}}
                Discount: {{totals.Discount}}
                VAT: {{totals.Vat}}
                Gross total: {{totals.Total}}
                Contract period: {{contract.StartDate?.ToString("yyyy-MM-dd") ?? "not set"}} to {{contract.EndDate?.ToString("yyyy-MM-dd") ?? "not set"}}

                Positions:
                {{JsonSerializer.Serialize(payload)}}

                {{(string.IsNullOrWhiteSpace(options.AdditionalInstructions)
                    ? ""
                    : "Additional guidance for the wording only:\n" + options.AdditionalInstructions)}}
                """;
        }

        private static ContractDraftSummary ToSummary(ContractDraft d, PositionTotalsDto? totals) =>
            new(d.Id, d.Version, d.DocumentMarkdown, d.Model, d.PromptVersion, d.TemplateVersion,
                d.GeneratedBy, d.GeneratedAt, d.IsApproved, d.ApprovedAt, d.DocumentHash, totals)
            {
                // Older rows predate the Kind column and carry the provenance in
                // GeneratedBy, so a supplied document imported before this change
                // is still recognised as one.
                Kind = d.Kind == ContractDraftKind.Generated && d.GeneratedBy == "pasted"
                    ? ContractDraftKind.Supplied
                    : d.Kind,
                IsImmutableSource = d.IsImmutableSource || d.GeneratedBy == "pasted",
                SourceLanguage = d.SourceLanguage,
                ExtractionStatus = d.ExtractionStatus,

                // Rows written before the status column existed carry the fact in
                // IsApproved, so an older approved version still reads as approved.
                Status = d.Status == ContractDraftStatus.Draft && d.IsApproved
                    ? ContractDraftStatus.Approved
                    : d.Status
            };

        /// <summary>
        /// The hash used to show that the text that was signed was the text that
        /// was approved. Public because the signing page records it too.
        /// </summary>
        public static string Sha256(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}

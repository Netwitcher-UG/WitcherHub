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
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Domain.Commercial;
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
        private readonly AppDbContext _db;
        private readonly IContractPositions _positions;
        private readonly IAiTextGenerator _ai;
        private readonly ISemanticContractAnalyzer _analyzer;
        private readonly OpenAIOptions _openAi;
        private readonly ContractTemplateOptions _template;
        private readonly ILogger<ContractDraftService> _logger;
        private readonly IBackgroundAnalysisRunner? _background;
        private readonly ContractContextBuilder _contextBuilder;

        public ContractDraftService(
            AppDbContext db,
            IContractPositions positions,
            IAiTextGenerator ai,
            ISemanticContractAnalyzer analyzer,
            IOptions<OpenAIOptions> openAi,
            IOptions<ContractTemplateOptions> template,
            ILogger<ContractDraftService> logger,

            // Optional so the many tests that construct this service directly keep
            // working. Without it StartAnalysisAsync runs the reading inline,
            // which is what a test wants anyway.
            IBackgroundAnalysisRunner? background = null)
        {
            _db = db;
            _positions = positions;
            _ai = ai;
            _analyzer = analyzer;
            _openAi = openAi.Value;
            _template = template.Value;
            _logger = logger;
            _background = background;

            // Built here rather than injected: it is a pure assembler over the
            // same context and options this service already holds, and making it
            // a registered dependency would break every test that constructs this
            // service directly for no benefit.
            _contextBuilder = new ContractContextBuilder(db, _template);
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

            // One click is one version, checked before anything else happens.
            //
            // This check used to live inside the supplied-text path, which was the
            // only path that could be reached twice. Once every generation went
            // through the model, a double click became two model calls and two
            // versions — so the guard belongs here, above every path, where the
            // request is what is being de-duplicated rather than the route it
            // happens to take.
            if (TryFindAlreadyGenerated(contract, options.IdempotencyKey) is { } repeated)
            {
                _logger.LogInformation(
                    "Generation request {Key} for contract {ContractId} was already carried out as v{Version}.",
                    options.IdempotencyKey, contractId, repeated.Version);

                return new ContractDraftResult
                {
                    Succeeded = true,
                    Draft = ToSummary(repeated, null),
                    WasAlreadyPrepared = true
                };
            }

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

            // Pasted text is an input to generation, never the output of it.
            //
            // This is the bug the owner reported as two stacked contracts. There
            // were three paths and two of them put the pasted document into the
            // contract body: Hybrid concatenated `source + "---" + generated`, and
            // SuppliedText copied the merged source and appended a terms block. So
            // the preview showed a wall of somebody's old agreement and then, below
            // a rule, the actual contract.
            //
            // There is now one path. Whatever sources exist — positions, the
            // record, confirmed terms, and the pasted text if there is any — are
            // assembled into one context and produce one contract. Preparation
            // without a model is still available and is still deterministic, but it
            // is reached only when generation is impossible, not whenever a
            // document happens to have been pasted.
            var sourceText = LatestSupplied(contract)?.DocumentMarkdown;

            var context = await _contextBuilder.BuildAsync(
                contract,
                positions,
                totals,
                sourceText,
                options.AdditionalInstructions,
                options.Language,
                ct);

            string document;
            try
            {
                document = await _ai.GenerateTextAsync(ContractGeneratorPrompt.Build(context));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                // A fault the assistant will not recover from on its own — no API
                // key, no credit, an unusable model — produces a contract anyway,
                // composed from the record with no model involved. Otherwise the
                // work stops until somebody fixes billing, which is the thing that
                // must not happen.
                //
                // A transient fault does not take this path on purpose. A rate
                // limit clears in seconds, and quietly substituting a plainer
                // contract there risks somebody approving and sending it without
                // noticing they got the lesser version. Those are reported and
                // retried instead — and nothing about them blocks editing details,
                // positions, the source, or the wording by hand.
                if (ex.NeedsOwnerAction)
                {
                    _logger.LogWarning(
                        "Falling back to a composed contract for {ContractId}: {Kind} ({CorrelationId}).",
                        contractId, ex.Kind, ex.CorrelationId);

                    var fallback = await ComposeWithoutAiAsync(contract, options, ct);

                    if (fallback.Succeeded)
                    {
                        // Succeeded, but with something worth saying: the contract
                        // exists and is plainer than it would have been.
                        return new ContractDraftResult
                        {
                            Succeeded = true,
                            Draft = fallback.Draft,
                            Money = fallback.Money,
                            ComposedWithoutAi = true,
                            FailureReason =
                                ex.UserMessage + " A contract was composed from your positions and details " +
                                "instead — review it, and regenerate once the assistant is working."
                        };
                    }
                }

                // Everything the user entered is already saved; only the wording
                // step failed. The message says which failure it was and carries
                // the reference that finds it in the log.
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

            // The model returns clause content as JSON; the document is composed
            // here. It used to return the finished Markdown, which meant it chose
            // the heading levels, the numbering and whether the parties appeared at
            // all — so two contracts generated a minute apart did not look like the
            // same company's contracts.
            if (!GeneratedContractContent.TryParse(document, out var content, out var parseError))
            {
                _logger.LogWarning(
                    "Contract generation for {ContractId} returned unusable content: {Error}",
                    contractId, parseError);

                return ContractDraftResult.Failed(
                    "The assistant's answer could not be read as a contract. Everything you entered is " +
                    "saved — try again, or write the contract text by hand.",
                    transient: true);
            }

            var parties = await BuildPartyDetailsAsync(contract, ct);

            // One contract, from all the sources. Nothing is appended to it: the
            // pasted document informed the clauses and is not part of them.
            document = GermanContractDocument.Compose(
                title: string.IsNullOrWhiteSpace(content.ContractType)
                    ? GeneratedContractTitle
                    : content.ContractType!.Trim(),
                contractNo: contract.ContractNo,
                projectTitle: contract.Project?.Title,
                parties: new GermanContractDocument.Parties(
                    parties.CompanyName,
                    parties.CompanyAddress,
                    parties.CustomerName,
                    parties.CustomerAddress),
                clauses: content.ToClauseMarkdown(),
                start: contract.StartDate,
                end: contract.EndDate);

            var draft = new ContractDraft
            {
                ContractId = contractId,
                Version = await NextVersionAsync(contractId, ct),
                DocumentMarkdown = document.Trim(),
                PositionsSnapshot = JsonSerializer.SerializeToDocument(positions),
                PromptVersion = ContractGeneratorPrompt.Version,
                TemplateVersion = _template.BaseDePath,
                Model = _openAi.Model,
                GeneratedBy = "openai",
                GeneratedAt = DateTimeOffset.UtcNow,
                Kind = ContractDraftKind.Generated,

                // Which source document informed this contract, recorded as a
                // reference rather than as content. It is what makes "generated
                // with the pasted text in mind" answerable without the pasted text
                // being in the contract.
                SourceDraftId = LatestSupplied(contract)?.Id
            };

            _db.Set<ContractDraft>().Add(draft);

            // Recorded so the guard at the top of this method can recognise a
            // repeat. Without this the guard could never match on the generated
            // path, and only the composed path was ever de-duplicated.
            contract.LastPreparationKey = options.IdempotencyKey;
            contract.LastPreparedDraftId = draft.Id;

            // A draft exists, whichever path produced it.
            //
            // These two used to be written only by the supplied-text path, so once
            // every contract went through generation the workflow state stopped
            // advancing and the party record stopped being kept. Both belong to
            // "a version was produced", not to how it was produced.
            contract.PreparationState = ContractPreparationState.PreparedDraft;

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
                "Generated draft v{Version} for contract {ContractId} from {Mode} using {Model}. " +
                "Source document {Source} informed it without being copied into it.",
                draft.Version, contractId, source.Mode, _openAi.Model,
                draft.SourceDraftId is null ? "was absent" : "v" + LatestSupplied(contract)?.Version);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, totals) };
        }

        /// <summary>
        /// Composes a contract from the record, with no model involved.
        ///
        /// The path that keeps the work moving when the assistant cannot be used:
        /// a missing API key, an empty account, a model the account cannot reach.
        /// Everything in the document comes from the positions, the confirmed terms
        /// and the parties, so it claims nothing that is not in the record — and it
        /// needs no pasted document, because a contract built from positions and
        /// details is a perfectly ordinary contract.
        ///
        /// This used to require a supplied document and merge the parties into it,
        /// which made the pasted text the contract body. Both of those are gone:
        /// pasted text is a source for generation, and this composes rather than
        /// copies.
        /// </summary>
        private async Task<ContractDraftResult> ComposeWithoutAiAsync(
            Contract contract, GenerateDraftOptions options, CancellationToken ct)
        {
            // Referenced when there is one, not required. Requiring it meant a
            // contract with positions and no pasted document could not be composed
            // at all — so an unusable API key blocked exactly the case that needs
            // no assistant.
            var supplied = LatestSupplied(contract);

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

            if (string.IsNullOrWhiteSpace(parties.CompanyName) ||
                string.IsNullOrWhiteSpace(parties.CustomerName))
            {
                contract.PreparationState = ContractPreparationState.PreparationFailed;
                await _db.SaveChangesAsync(ct);

                return ContractDraftResult.Failed(
                    "A contract needs both parties named. Check the company settings and the customer " +
                    "record, then try again.");
            }

            // Composed from the record, not copied from the pasted document.
            //
            // This path used to merge the current parties into the supplied text
            // and call the result the contract, so whatever somebody pasted became
            // the contract body. That is the behaviour the owner ruled out: pasted
            // text is a source for generation and never the output of it. The
            // clauses here come from the positions and the confirmed terms — the
            // things a person entered or agreed to — which is also why this path
            // needs no model and works when the assistant is unreachable.
            var positions = await _positions.GetPositionsAsync(contract.Id, ct);
            var totals = _positions.CalculateTotals(positions, contract.Currency);

            var clauses = BuildDeterministicClauses(contract, positions, totals,
                await BuildConfirmedTermListAsync(contract, ct));

            var document = GermanContractDocument.Compose(
                title: GeneratedContractTitle,
                contractNo: contract.ContractNo,
                projectTitle: contract.Project?.Title,
                parties: new GermanContractDocument.Parties(
                    parties.CompanyName,
                    parties.CompanyAddress,
                    parties.CustomerName,
                    parties.CustomerAddress),
                clauses: clauses,
                start: contract.StartDate,
                end: contract.EndDate);

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
                SourceDraftId = supplied?.Id,
                SourceLanguage = supplied?.SourceLanguage
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
                "Composed contract v{Version} for {ContractId} without a model from {Positions} position(s) " +
                "and {Terms} confirmed term(s). Source document v{Source} was referenced, not copied.",
                draft.Version, contract.Id, positions.Count, clauses.Length,
                supplied is null ? "(none)" : "v" + supplied.Version);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        /// <summary>
        /// The version an identical earlier request already produced, or null.
        ///
        /// Keyed on what the caller sent rather than on what the contract looks
        /// like now, so a retry after a timeout returns the version the first
        /// attempt made instead of adding a second one.
        /// </summary>
        private static ContractDraft? TryFindAlreadyGenerated(Contract contract, string? idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;

            if (!string.Equals(contract.LastPreparationKey, idempotencyKey, StringComparison.Ordinal))
                return null;

            return contract.LastPreparedDraftId is { } id
                ? contract.Drafts.FirstOrDefault(d => d.Id == id)
                : null;
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

            // The semantic pipeline: classify what the document contains, structure
            // only the parts that are charges, validate without discarding partial
            // readings, then calculate. The arithmetic is the engine's, not the
            // model's, so the figures below can be reproduced and defended.
            var semantic = await _analyzer.AnalyzeAsync(
                draft.DocumentMarkdown,
                new SemanticAnalysisOptions
                {
                    LanguageHint = draft.SourceLanguage,
                    FallbackCurrency = contract.Currency ?? "EUR",

                    // Lets a monthly charge with no stated end be totalled against
                    // the contract's own term instead of being left uncalculable.
                    ContractMonths = MonthsBetween(contract.StartDate, contract.EndDate)
                },
                ct);

            if (!semantic.Succeeded)
            {
                // A failed reading is recorded as a failed reading. The document
                // itself is untouched and remains the contract's source, so the
                // user can carry on with the original text.
                var reason = semantic.FailureReason ?? "The document could not be analysed.";

                draft.ExtractionStatus = ContractExtractionStatus.Failed;

                // Kept, not just returned. The reading now finishes after the
                // request that asked for it has gone, so the reason has to
                // survive in the row or the page has nothing to show — and a
                // reload turned a nameable failure back into "never analysed".
                draft.ExtractionError = Truncate(reason, 1000);
                draft.ExtractionErrorIsTransient = semantic.IsTransientFailure;
                draft.ExtractionStartedAt = null;

                contract.SourceState = ContractSourceState.AnalysisFailed;

                await _db.SaveChangesAsync(ct);

                return ContractAnalysisResult.Failed(
                    reason,
                    semantic.IsTransientFailure,
                    semantic.CorrelationId);
            }

            // Everything the reading produced, kept whole.
            draft.SemanticAnalysis = JsonSerializer.SerializeToDocument(
                new
                {
                    schema = SemanticAnalysisSchema,
                    analysedAt = DateTimeOffset.UtcNow,
                    model = semantic.Model,
                    promptVersion = semantic.PromptVersion,
                    extraction = semantic.Extraction,
                    terms = semantic.Terms,
                    issues = semantic.Issues,
                    discarded = semantic.DiscardedReasons,
                    financials = semantic.Financials
                },
                SemanticAnalysisJson);

            // And the same reading in the shape the review screen reads, so that
            // screen keeps working while it is replaced.
            var projected = SemanticExtractionProjection.ToLegacyExtraction(semantic);

            draft.ExtractedTerms = JsonSerializer.SerializeToDocument(projected);
            draft.ExtractionStatus = ContractExtractionStatus.Analysed;
            draft.ExtractedAt = DateTimeOffset.UtcNow;
            draft.ExtractionStartedAt = null;
            draft.ExtractionError = null;
            draft.ExtractionErrorIsTransient = null;

            if (projected.Language.HasValue)
                draft.SourceLanguage = projected.Language.Value;

            contract.SourceState = ContractSourceState.Analysed;
            contract.ReviewState = ContractReviewState.RequiresReview;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Analysed supplied contract v{Version} of {ContractId}: {Concepts} concept(s), {Terms} billable " +
                "term(s), {Warnings} warning(s), {Unresolved} unresolved amount(s), committed net {Committed}.",
                version, contractId,
                semantic.Extraction?.Concepts.Count ?? 0,
                semantic.Terms.Count,
                projected.Warnings.Count,
                semantic.Financials?.Unresolved.Count ?? 0,
                semantic.Financials?.CommittedNet);

            return new ContractAnalysisResult
            {
                Succeeded = true,
                Extraction = projected,
                Model = semantic.Model,
                CorrelationId = semantic.CorrelationId
            };
        }

        /// <summary>
        /// How long a reading may say it is running before we stop believing it.
        ///
        /// The queue is in-process, so a restart or a crash loses whatever was in
        /// flight and leaves the row saying "analysing" for ever. Past this, the
        /// draft is treated as failed and can be started again — which is the
        /// only way out, since there is nothing left to wait for.
        /// </summary>
        internal static readonly TimeSpan AnalysisAbandonedAfter = TimeSpan.FromMinutes(20);

        public async Task<ContractAnalysisStart> StartAnalysisAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var draft = await _db.Set<ContractDraft>()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct)
                ?? throw new NotFoundAppException("Draft not found.");

            if (string.IsNullOrWhiteSpace(draft.DocumentMarkdown))
                return ContractAnalysisStart.Refused("There is no contract text on this version to analyse.");

            // A second press joins the reading already running rather than paying
            // for another one. The stale check is what stops a lost worker from
            // making that a permanent refusal.
            if (draft.ExtractionStatus == ContractExtractionStatus.Analysing &&
                !HasBeenAbandoned(draft))
            {
                return ContractAnalysisStart.Joined();
            }

            draft.ExtractionStatus = ContractExtractionStatus.Analysing;
            draft.ExtractionStartedAt = DateTimeOffset.UtcNow;
            draft.ExtractionError = null;
            draft.ExtractionErrorIsTransient = null;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Analysis of v{Version} of {ContractId} queued ({Length} characters).",
                version, contractId, draft.DocumentMarkdown.Length);

            if (_background is null)
            {
                // No queue configured — do it here. The caller still polls; it
                // will simply find the answer already waiting.
                await AnalyzeAsync(contractId, version, ct);
                return ContractAnalysisStart.Started();
            }

            await _background.RunAsync(contractId, version);

            return ContractAnalysisStart.Started();
        }

        public async Task<ContractAnalysisProgress> GetAnalysisProgressAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var draft = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct)
                ?? throw new NotFoundAppException("Draft not found.");

            if (draft.ExtractionStatus == ContractExtractionStatus.Analysing && HasBeenAbandoned(draft))
            {
                // Reported as failed rather than left spinning for ever. The row
                // is corrected on the next start; saying so here keeps this a
                // read.
                return new ContractAnalysisProgress
                {
                    Status = ContractExtractionStatus.Failed,
                    IsTransientFailure = true,
                    FailureReason =
                        "The analysis stopped before it finished, most likely because the application " +
                        "restarted while it was running. Your document is unchanged — start it again."
                };
            }

            return new ContractAnalysisProgress
            {
                Status = draft.ExtractionStatus,
                FailureReason = draft.ExtractionError,

                // As the reading judged it, not as this method guesses. Saying
                // "worth retrying" about a missing API key is what leaves an
                // owner pressing a button that cannot succeed. Older rows have
                // no stored answer; those are retryable, which is the safe way
                // to be wrong.
                IsTransientFailure =
                    draft.ExtractionStatus == ContractExtractionStatus.Failed &&
                    draft.ExtractionErrorIsTransient != false,

                Extraction = draft.ExtractionStatus is ContractExtractionStatus.Analysed
                                                    or ContractExtractionStatus.Confirmed
                    ? ReadExtraction(draft.ExtractedTerms, contractId, version)
                    : null,

                Elapsed = draft.ExtractionStartedAt is { } started
                    ? DateTimeOffset.UtcNow - started
                    : null
            };
        }

        private static bool HasBeenAbandoned(ContractDraft draft) =>
            draft.ExtractionStartedAt is null ||
            DateTimeOffset.UtcNow - draft.ExtractionStartedAt.Value > AnalysisAbandonedAfter;

        /// <summary>
        /// Identifies the shape stored in <see cref="ContractDraft.SemanticAnalysis"/>,
        /// so a later reader can tell what it is looking at rather than inferring it
        /// from which fields happen to be present.
        /// </summary>
        public const string SemanticAnalysisSchema = "semantic-analysis-v1";

        /// <summary>
        /// Stated rather than left to the default, because this document is read
        /// back by name — by a later reader here, and by anyone querying the jsonb
        /// column directly. Property casing that depends on the serialiser's
        /// default is a shape nobody chose and nothing can rely on.
        /// </summary>
        internal static readonly JsonSerializerOptions SemanticAnalysisJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// The contract's length, where both ends are known. Null otherwise — a
        /// term the contract does not state must not be invented here either, and
        /// a guessed length would silently change every recurring total.
        /// </summary>
        private static int? MonthsBetween(DateOnly? start, DateOnly? end)
        {
            if (start is null || end is null || end <= start) return null;

            var months = ((end.Value.Year - start.Value.Year) * 12) + end.Value.Month - start.Value.Month;

            return months > 0 ? months : null;
        }

        public async Task<ContractExtractionDto?> GetExtractionAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var stored = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId && d.Version == version)
                .Select(d => d.ExtractedTerms)
                .FirstOrDefaultAsync(ct);

            return ReadExtraction(stored, contractId, version);
        }

        /// <summary>
        /// The stored extraction, or null when it cannot be read. An extraction
        /// written in an older shape is not worth failing a page over — the user
        /// can analyse again.
        /// </summary>
        private ContractExtractionDto? ReadExtraction(JsonDocument? stored, Guid contractId, int version)
        {
            if (stored is null) return null;

            try
            {
                return stored.Deserialize<ContractExtractionDto>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stored extraction for {ContractId} v{Version} could not be read.",
                    contractId, version);
                return null;
            }
        }

        public async Task<ContractFinancials?> GetFinancialsAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var stored = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId && d.Version == version)
                .Select(d => d.SemanticAnalysis)
                .FirstOrDefaultAsync(ct);

            if (stored is null) return null;

            try
            {
                if (!stored.RootElement.TryGetProperty("financials", out var financials))
                    return null;

                return financials.Deserialize<ContractFinancials>(SemanticAnalysisJson);
            }
            catch (JsonException ex)
            {
                // A reading stored under an earlier shape. Not worth failing a page
                // over: the figures are absent, which the caller shows as unknown
                // rather than as zero.
                _logger.LogWarning(ex,
                    "Stored semantic analysis for {ContractId} v{Version} could not be read.",
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

            // ------------------------------------------------------------------
            // What the record already says wins.
            //
            // A confirmed reading used to be written straight over the contract,
            // so a start date somebody had entered on the project was replaced by
            // whatever the supplied PDF happened to say, silently and with no way
            // back. That is the wrong way round: the details captured against the
            // project and the contract are the ones the business decided on, and
            // the document is a source for the gaps in them.
            //
            // So a reading now fills what is empty and never overwrites what is
            // not. Everything the document says is still stored on the draft and
            // still shown in the review, so nothing is lost — what changes is
            // which one wins when they disagree.
            //
            // Only confirmed values are considered at all. An unconfirmed reading
            // stays a reading.
            // ------------------------------------------------------------------

            var keptFromRecord = new List<string>();

            if (confirmed.TotalPrice.Confirmed &&
                ContractTextAnalyzer.TryParseAmount(confirmed.TotalPrice.Value, out var total))
            {
                if (contract.AgreedTotalNet is null && !contract.PriceDeliberatelyUnspecified)
                {
                    contract.AgreedTotalNet = total;
                    contract.PriceDeliberatelyUnspecified = false;
                }
                else if (contract.AgreedTotalNet != total)
                {
                    keptFromRecord.Add("total price");
                }
            }

            if (confirmed.VatRate.Confirmed &&
                ContractTextAnalyzer.TryParseAmount(confirmed.VatRate.Value, out var vat))
            {
                if (contract.AgreedTotalVatRatePercent is null)
                    contract.AgreedTotalVatRatePercent = vat;
                else if (contract.AgreedTotalVatRatePercent != vat)
                    keptFromRecord.Add("VAT rate");
            }

            if (confirmed.Currency.Confirmed && confirmed.Currency.HasValue)
            {
                var fromText = NormaliseCurrency(confirmed.Currency.Value!);

                if (string.IsNullOrWhiteSpace(contract.Currency))
                    contract.Currency = fromText ?? contract.Currency;
                else if (fromText is not null && !string.Equals(fromText, contract.Currency, StringComparison.OrdinalIgnoreCase))
                    keptFromRecord.Add("currency");
            }

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
            {
                if (string.IsNullOrWhiteSpace(contract.PaymentTermsText))
                    contract.PaymentTermsText = Truncate(paymentTerms, 2000);
                else
                    keptFromRecord.Add("payment terms");
            }

            // A price that is deliberately absent is a decision, recorded as one.
            // This is the user saying so on this screen, not the document, so it
            // is not subject to the rule above.
            if (confirmed.PriceMissing && !confirmed.TotalPrice.HasValue)
            {
                contract.AgreedTotalNet = null;
                contract.PriceDeliberatelyUnspecified = confirmed.TotalPrice.Confirmed;
            }

            if (confirmed.StartDate.Confirmed && TryParseDate(confirmed.StartDate.Value, out var start))
            {
                if (contract.StartDate is null) contract.StartDate = start;
                else if (contract.StartDate != start) keptFromRecord.Add("start date");
            }

            if (confirmed.EndDate.Confirmed && TryParseDate(confirmed.EndDate.Value, out var end))
            {
                if (contract.EndDate is null) contract.EndDate = end;
                else if (contract.EndDate != end) keptFromRecord.Add("end date");
            }

            // A date the contract took from the document is worth having on the
            // project too, where the dates are otherwise entered by hand — but
            // again only into a gap.
            var filledOnProject = await FillProjectGapsAsync(contract, ct);

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
                KeptFromRecord = keptFromRecord,
                FilledOnProject = filledOnProject,
                Money = new ContractMoneyDto(
                    contract.AgreedTotalNet,
                    contract.AgreedTotalVatRatePercent,
                    contract.Currency,
                    contract.PriceDeliberatelyUnspecified)
            };
        }

        /// <summary>
        /// Puts the contract's dates onto its project where the project has none.
        ///
        /// The project is where these are normally entered, and a project created
        /// alongside a supplied contract often has them blank. Filling only the
        /// blanks keeps the same rule as everywhere else here: what somebody
        /// entered stands, and a document may only answer what nobody has.
        /// </summary>
        private async Task<IReadOnlyList<string>> FillProjectGapsAsync(
            Contract contract, CancellationToken ct)
        {
            if (contract.ProjectId == Guid.Empty) return Array.Empty<string>();

            var project = await _db.Set<Project>()
                .FirstOrDefaultAsync(p => p.Id == contract.ProjectId, ct);

            if (project is null) return Array.Empty<string>();

            var filled = new List<string>();

            if (project.StartDate is null && contract.StartDate is not null)
            {
                project.StartDate = contract.StartDate;
                filled.Add("start date");
            }

            if (project.EndDate is null && contract.EndDate is not null)
            {
                project.EndDate = contract.EndDate;
                filled.Add("end date");
            }

            return filled;
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

                // The project's title goes into the composed document's reference
                // line. Without this include it was silently null there.
                .Include(c => c.Project)
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

        // The old free-form prompt lived here.
        //
        // It asked the model for the finished document as Markdown, which is why
        // the model decided the heading levels, the numbering and whether the
        // parties appeared at all — and why its output had to be stapled to a
        // pasted document to look complete. Generation is now one prompt in one
        // place, ContractGeneratorPrompt, returning structured clauses that this
        // service composes into the document.
        /// <summary>
        /// The clauses of a contract, written without a model.
        ///
        /// Plain and factual on purpose. This is what a contract looks like when
        /// the assistant is unreachable or not configured: every figure comes from
        /// the positions and the confirmed terms, and no sentence claims anything
        /// that is not in the record. It is a usable contract that a person can
        /// then edit, which is the point — the work must not stop because OpenAI
        /// is down.
        /// </summary>
        private static string BuildDeterministicClauses(
            Contract contract,
            IReadOnlyList<ManualPositionDto> positions,
            PositionTotalsDto totals,
            IReadOnlyList<(string Label, string Value)> confirmedTerms)
        {
            var md = new StringBuilder();
            var german = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            md.Append("## § 1 Gegenstand des Vertrags\n\n");
            md.Append("Der Auftragnehmer erbringt für den Auftraggeber die nachstehend aufgeführten Leistungen.\n\n");

            if (positions.Count > 0)
            {
                md.Append("## § 2 Leistungsumfang\n\n");

                var n = 0;

                foreach (var p in positions.OrderBy(p => p.Position))
                {
                    n++;

                    var line = new StringBuilder();
                    line.Append('(').Append(n).Append(") ").Append(p.Title?.Trim());

                    if (!string.IsNullOrWhiteSpace(p.Description))
                        line.Append(" — ").Append(p.Description.Trim());

                    md.Append(line).Append("\n\n");

                    if (!string.IsNullOrWhiteSpace(p.Scope))
                        md.Append(p.Scope.Trim()).Append("\n\n");
                }

                md.Append("## § 3 Vergütung und Zahlung\n\n");

                var money = 0;

                foreach (var p in positions.OrderBy(p => p.Position))
                {
                    money++;

                    var amount = p.IsFree
                        ? "ohne Berechnung"
                        : p.UnitPrice is null
                            ? "wird noch festgelegt"
                            : $"{p.UnitPrice.Value.ToString("N2", german)} {p.Currency ?? contract.Currency ?? "EUR"}";

                    md.Append('(').Append(money).Append(") ")
                      .Append(p.Title?.Trim()).Append(": ").Append(amount)
                      .Append(BillingCycleText(p.BillingCycle))
                      .Append(".\n\n");
                }

                md.Append('(').Append(money + 1).Append(") Nettosumme: ")
                  .Append(totals.Subtotal.ToString("N2", german)).Append(' ').Append(totals.Currency)
                  .Append(", Umsatzsteuer: ").Append(totals.Vat.ToString("N2", german))
                  .Append(", Gesamtbetrag: ").Append(totals.Total.ToString("N2", german))
                  .Append(' ').Append(totals.Currency).Append(".\n\n");
            }

            if (confirmedTerms.Count > 0)
            {
                md.Append("## § 4 Vereinbarte Konditionen\n\n");

                foreach (var (label, value) in confirmedTerms)
                    md.Append("- ").Append(label).Append(": ").Append(value).Append('\n');

                md.Append('\n');
            }

            return md.ToString().TrimEnd() + "\n";
        }

        private static string BillingCycleText(BillingCycle cycle) => cycle switch
        {
            BillingCycle.Monthly => " monatlich",
            BillingCycle.Quarterly => " vierteljährlich",
            BillingCycle.SemiAnnual => " halbjährlich",
            BillingCycle.Annual => " jährlich",
            _ => ""
        };

        /// <summary>
        /// The confirmed commercial terms as label/value pairs, for the
        /// deterministic path. Same source as the generator's context.
        /// </summary>
        private async Task<IReadOnlyList<(string Label, string Value)>> BuildConfirmedTermListAsync(
            Contract contract, CancellationToken ct)
        {
            var context = await _contextBuilder.BuildAsync(
                contract, Array.Empty<ManualPositionDto>(), null, null, null, "de", ct);

            return context.ConfirmedTerms.Select(t => (t.Label, t.Value)).ToList();
        }

        /// <summary>
        /// What this contract is called at the head of the document.
        ///
        /// A German contract names its type on the first line. The project name
        /// in that position — which is what a generated document used to open
        /// with — reads as a web page, not as a Vertrag.
        /// </summary>
        private const string GeneratedContractTitle = "Dienstleistungsvertrag";

        /// <summary>
        /// Removes a title, party block or signature block the model produced
        /// anyway, so the composed ones are not shown twice.
        ///
        /// The prompt tells it not to, and mostly it does not — but a document
        /// with two titles is the sort of thing a customer notices, and dropping
        /// a stray heading costs nothing. Only a leading level-1 heading and the
        /// lines around it are touched; the §§ are never altered.
        /// </summary>
        internal static string StripComposedParts(string clauses)
        {
            var lines = clauses.Replace("\r\n", "\n").Split('\n').ToList();

            // Anything before the first § heading is frame the model was asked
            // not to write. Kept only if there is no § at all, because then this
            // is all we have and showing it beats showing nothing.
            var firstClause = lines.FindIndex(l => l.TrimStart().StartsWith("## §", StringComparison.Ordinal));

            if (firstClause > 0)
                lines.RemoveRange(0, firstClause);

            // A trailing signature block, if it wrote one.
            var signature = lines.FindIndex(l =>
                l.Contains("Ort, Datum", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Unterschrift", StringComparison.OrdinalIgnoreCase));

            if (signature > 0 && !lines[signature].TrimStart().StartsWith("## §", StringComparison.Ordinal))
                lines.RemoveRange(signature, lines.Count - signature);

            return string.Join("\n", lines).Trim();
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

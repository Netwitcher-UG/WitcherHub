using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Generating a contract, as a sequence of stages that can each be checked.
    ///
    /// It used to be one call. The prompt named seven headings, the model answered
    /// with seven short clauses, and whatever came back was saved as a finished
    /// version — so a contract covering thirty agreed items and a contract
    /// covering three came out the same length, both fitted on one page, and
    /// nothing anywhere compared the document to what had been agreed.
    ///
    /// The stages are: read the source, enumerate everything the contract must
    /// account for, plan the sections against that ledger, write the sections a
    /// few at a time, measure the result against the ledger, and repair what is
    /// missing. Length is never a target at any stage; it is what the ledger
    /// requires.
    ///
    /// Nothing here decides how the document looks. It produces clause content;
    /// the frame, the numbering and the typography stay where they were.
    /// </summary>
    public sealed class ContractGenerationPipeline
    {
        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _options;
        private readonly ILogger _logger;

        /// <summary>
        /// Takes a plain <see cref="ILogger"/> rather than a typed one so that the
        /// service which owns it can build it directly, the way it builds the
        /// context assembler. Making this a registered dependency would mean
        /// changing every test that constructs that service by hand, for no gain.
        /// </summary>
        public ContractGenerationPipeline(
            IAiTextGenerator ai,
            OpenAIOptions options,
            ILogger logger)
        {
            _ai = ai;
            _options = options;
            _logger = logger;
        }

        public ContractGenerationPipeline(
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> options,
            ILogger<ContractGenerationPipeline> logger)
            : this(ai, options.Value, logger)
        {
        }

        /// <summary>
        /// Where the leftovers of a plan go — entries the model forgot to assign.
        /// A real heading rather than a bucket name, because it is printed.
        /// </summary>
        private const string LeftoverHeading = "Weitere Vereinbarungen";

        /// <summary>
        /// How many section calls may be in flight together.
        ///
        /// Enough that a long contract is not written one batch at a time while
        /// somebody watches; few enough that generating one does not open a dozen
        /// simultaneous requests and earn a rate limit — which would cost more
        /// time than it saved.
        /// </summary>
        private const int ParallelCalls = 3;

        /// <summary>
        /// Below this share of the ledger, a repair pass is worth another call.
        ///
        /// Above it the document already says nearly everything, and the gaps are
        /// reported to the reviewer rather than chased with a model call the user
        /// is waiting on. Anything critical is repaired at any coverage.
        /// </summary>
        private const double RepairBelowCoverage = 0.9;

        public async Task<ContractGenerationOutcome> RunAsync(
            ContractGenerationContext context,
            CancellationToken ct = default)
        {
            var started = Stopwatch.GetTimestamp();
            var telemetry = new ContractGenerationTelemetry { PromptVersion = ContractGeneratorPrompt.Version };

            // ---- 1. the ledger, from the record -----------------------------

            var ledger = ContractCoverageLedger.FromRecord(context);

            // ---- 2. the source, read rather than pasted ---------------------

            if (context.HasSourceText)
                ledger = await AddSourceTopicsAsync(context, ledger, telemetry, ct);

            telemetry.LedgerItems = ledger.Count;

            _logger.LogInformation(
                "Contract generation: {Items} coverage entries ({Commercial} commercial) from " +
                "{Positions} positions, {Terms} confirmed terms, source text {Source}.",
                ledger.Count,
                ledger.Commercial.Count(),
                context.Positions.Count,
                context.ConfirmedTerms.Count,
                context.HasSourceText ? $"{context.SourceText!.Length} chars" : "absent");

            // ---- 3. the plan ------------------------------------------------

            var outline = await PlanAsync(context, ledger, telemetry, ct);

            var forgotten = outline.Unassigned(ledger);

            if (forgotten.Count > 0)
            {
                _logger.LogWarning(
                    "Contract plan left {Count} coverage entries unassigned; adding them to \"{Heading}\". Ids: {Ids}",
                    forgotten.Count, LeftoverHeading, string.Join(", ", forgotten.Select(i => i.Id)));

                outline.AssignLeftovers(forgotten, LeftoverHeading);
                telemetry.UnplannedEntries = forgotten.Count;
            }

            telemetry.PlannedSections = outline.Sections.Count;

            // ---- 4. the clauses, a few at a time and all at once -------------

            // The batches do not depend on one another: each was given its own
            // headings and its own ledger entries by the plan. Run one after the
            // other, a twelve-section contract cost three model calls end to end
            // and the user watched all three — which is most of why generation
            // went from slow to unusable. They go together now, bounded so a
            // large contract cannot open an unlimited number of calls at once.
            var batches = outline.InBatches(ContractGeneratorPrompt.SectionsPerCall).ToList();

            var written = (await WhenAll(batches, batch =>
                    WriteBatchAsync(context, ledger, batch, telemetry, ct)))
                .SelectMany(sections => sections)
                .ToList();

            if (written.Count == 0)
            {
                throw new AiInvocationException(
                    AiFailureKind.BadResponse,
                    "no section of the plan produced any text",
                    "PIPELINE",
                    null);
            }

            // ---- 5. the audit -----------------------------------------------

            var audit = Measure(ledger, outline, written);

            // ---- 6. one repair pass, when it is worth a call -----------------

            // Not on any gap at all. A repair is another whole model call on the
            // end of a generation the user is waiting for, and a contract that
            // covers everything agreed except one position's notes does not need
            // one. It runs when something that may never be dropped is missing, or
            // when enough is missing that the document is genuinely thin.
            if (audit.CriticalGaps.Count > 0 || audit.Ratio < RepairBelowCoverage)
            {
                _logger.LogInformation("Contract coverage before repair: {Summary}", audit.Summary);

                written = await RepairAsync(context, ledger, audit, written, telemetry, ct);
                audit = Measure(ledger, outline, written, written);
                telemetry.Repaired = true;
            }
            else if (!audit.IsComplete)
            {
                // Left alone, but not left unsaid: the gaps still reach the
                // reviewer, they simply do not cost another call to chase.
                _logger.LogInformation(
                    "Contract coverage {Summary}; close enough that a repair pass is not worth a further call.",
                    audit.Summary);
            }

            telemetry.WrittenSections = written.Count(s => s.HasContent);
            telemetry.CoverageRatio = audit.Ratio;
            telemetry.CriticalGaps = audit.CriticalGaps.Count;
            telemetry.ElapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            _logger.LogInformation(
                "Contract generation finished in {Elapsed}ms: {Calls} model calls, {In} input tokens, " +
                "{Out} output tokens, {Sections} sections written from {Planned} planned, coverage {Summary}." +
                "{Truncation}",
                telemetry.ElapsedMs, telemetry.ModelCalls, telemetry.InputTokens, telemetry.OutputTokens,
                telemetry.WrittenSections, telemetry.PlannedSections, audit.Summary,
                telemetry.TruncatedCalls == 0
                    ? ""
                    : $" {telemetry.TruncatedCalls} call(s) hit the output limit.");

            var content = new GeneratedContractContent
            {
                Language = context.Language,
                ContractType = outline.ContractType,
                Preamble = outline.Preamble,
                Sections = written.Where(s => s.HasContent).ToList()
            };

            return new ContractGenerationOutcome(content, ledger, audit, telemetry);
        }

        // ============================================================ stages

        private async Task<ContractCoverageLedger> AddSourceTopicsAsync(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            ContractGenerationTelemetry telemetry,
            CancellationToken ct)
        {
            var answer = await CallAsync(
                ContractGeneratorPrompt.SourceAnalysis(context), "contract.source-analysis", telemetry, ct);

            if (!SourceAnalysisResult.TryParse(answer.Text, out var analysis, out var error))
            {
                // A source document that could not be read is a thinner contract,
                // not a failed one: everything in the record is still there, and
                // refusing to generate would lose that too.
                _logger.LogWarning(
                    "The pasted document could not be analysed ({Error}); the contract will be built from " +
                    "the record alone.", error);

                telemetry.SourceAnalysisFailed = true;
                return ledger;
            }

            var topics = analysis.AsCoverageTopics().ToList();

            if (answer.IsTruncated)
            {
                _logger.LogWarning(
                    "The source analysis was cut off at the output limit after {Count} topics; the document " +
                    "may establish more than the contract accounts for.", topics.Count);
            }

            telemetry.SourceTopics = topics.Count;
            return ledger.WithSourceTopics(topics);
        }

        private async Task<ContractOutline> PlanAsync(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            ContractGenerationTelemetry telemetry,
            CancellationToken ct)
        {
            var answer = await CallAsync(
                ContractGeneratorPrompt.Outline(context, ledger), "contract.outline", telemetry, ct);

            if (ContractOutline.TryParse(answer.Text, out var outline, out var error))
                return outline;

            // A plan that was cut off mid-JSON and a plan the model garbled look
            // identical to a parser and need different words, because only one of
            // them has a setting behind it.
            if (answer.IsTruncated)
            {
                throw new AiInvocationException(
                    AiFailureKind.BadResponse,
                    $"the contract plan was cut off at the output limit; raise {OpenAIOptions.MaxOutputTokensSettingName}",
                    "PLAN-CUT",
                    null);
            }

            throw new AiInvocationException(
                AiFailureKind.BadResponse, $"the contract plan could not be read: {error}", "PLAN-BAD", null);
        }

        /// <summary>
        /// Writes one batch of planned sections.
        ///
        /// A batch that comes back cut off is split and retried rather than
        /// accepted: half a clause saved as a whole one is the failure this whole
        /// pipeline exists to prevent. A single section that still will not fit is
        /// reported and the rest of the contract goes on.
        /// </summary>
        private async Task<List<ContractSectionContent>> WriteBatchAsync(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            IReadOnlyList<ContractOutline.PlannedSection> batch,
            ContractGenerationTelemetry telemetry,
            CancellationToken ct)
        {
            var answer = await CallAsync(
                ContractGeneratorPrompt.Sections(context, ledger, batch), "contract.sections", telemetry, ct);

            if (answer.IsTruncated && batch.Count > 1)
            {
                var half = batch.Count / 2;

                _logger.LogWarning(
                    "A batch of {Count} sections was cut off at the output limit; retrying as two smaller batches.",
                    batch.Count);

                var first = await WriteBatchAsync(context, ledger, batch.Take(half).ToList(), telemetry, ct);
                var second = await WriteBatchAsync(context, ledger, batch.Skip(half).ToList(), telemetry, ct);

                return first.Concat(second).ToList();
            }

            if (!GeneratedContractContent.TryParse(answer.Text, out var content, out var error))
            {
                _logger.LogWarning(
                    "Sections [{Headings}] could not be read: {Error}. The audit will report them as missing.",
                    string.Join(", ", batch.Select(s => s.Heading)), error);

                return new List<ContractSectionContent>();
            }

            if (answer.IsTruncated)
            {
                _logger.LogWarning(
                    "Section \"{Heading}\" was cut off at the output limit even on its own. It is kept, and " +
                    "the audit reports what it does not cover.",
                    batch[0].Heading);
            }

            // The plan decides the order and the headings; a model that renamed one
            // would otherwise leave the audit unable to match its own plan.
            return Align(batch, content.Sections);
        }

        private async Task<List<ContractSectionContent>> RepairAsync(
            ContractGenerationContext context,
            ContractCoverageLedger ledger,
            ContractCoverageAudit audit,
            List<ContractSectionContent> written,
            ContractGenerationTelemetry telemetry,
            CancellationToken ct)
        {
            var headings = written.Select(s => s.Heading ?? "").Where(h => h.Length > 0).ToList();

            var answer = await CallAsync(
                ContractGeneratorPrompt.Repair(context, ledger, audit.Gaps, headings),
                "contract.repair", telemetry, ct);

            if (!GeneratedContractContent.TryParse(answer.Text, out var repair, out var error))
            {
                _logger.LogWarning(
                    "The repair pass produced nothing usable ({Error}); the draft is kept as it is and the " +
                    "gaps are recorded against it.", error);

                return written;
            }

            var merged = new List<ContractSectionContent>(written);

            foreach (var section in repair.Sections.Where(s => s.HasContent))
            {
                var existing = merged.FindIndex(s =>
                    string.Equals(s.Heading?.Trim(), section.Heading?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                    merged[existing] = section;
                else
                    merged.Add(section);
            }

            return merged;
        }

        // ============================================================ helpers

        /// <summary>
        /// One model call, with the system instruction, a budget, and the token
        /// counts recorded.
        /// </summary>
        private async Task<AiCompletion> CallAsync(
            string prompt, string purpose, ContractGenerationTelemetry telemetry, CancellationToken ct)
        {
            var answer = await _ai.CompleteAsync(new AiRequest(prompt)
            {
                SystemInstruction = ContractGeneratorPrompt.SystemInstruction,
                MaxOutputTokens = _options.EffectiveMaxOutputTokens,
                Purpose = purpose
            }, ct);

            // The section calls run together, so these are written from several
            // threads. Counting without this loses calls and tokens, which would
            // make the one measurement of how long generation costs unreliable
            // exactly when it matters.
            telemetry.Record(answer);

            return answer;
        }

        /// <summary>
        /// Runs the work together, a few at a time.
        ///
        /// Results come back in the order the inputs were given, whatever order
        /// they finish in — the plan decides the order of the sections, and a
        /// contract whose §§ arrived in completion order would be a different
        /// document each run.
        /// </summary>
        private static async Task<IReadOnlyList<T>> WhenAll<TItem, T>(
            IReadOnlyList<TItem> items, Func<TItem, Task<T>> work)
        {
            if (items.Count <= 1)
                return items.Count == 0 ? Array.Empty<T>() : [await work(items[0])];

            using var gate = new SemaphoreSlim(ParallelCalls, ParallelCalls);

            var tasks = items.Select(async item =>
            {
                await gate.WaitAsync();
                try { return await work(item); }
                finally { gate.Release(); }
            });

            return await Task.WhenAll(tasks);
        }

        private static ContractCoverageAudit Measure(
            ContractCoverageLedger ledger,
            ContractOutline outline,
            IReadOnlyList<ContractSectionContent> written,
            IReadOnlyList<ContractSectionContent>? claimOverride = null)
        {
            // What each section was asked to cover, paired with whether it came
            // back with anything. After a repair the sections' own declarations are
            // used instead, because a repaired section may cover more than the plan
            // gave it.
            var byHeading = written.ToDictionary(
                s => (s.Heading ?? "").Trim(),
                s => s.HasContent,
                StringComparer.OrdinalIgnoreCase);

            var plan = new List<ContractCoverageAudit.PlannedSection>();

            // The sections' own declarations go first, because the audit takes the
            // first claim it finds for an id. After a repair a section may cover
            // more than the plan gave it — or a new section may cover what an empty
            // one was supposed to — and reading the stale plan first would report
            // the repaired entry as still missing.
            if (claimOverride is not null)
            {
                foreach (var section in claimOverride.Where(s => s.Covers.Count > 0 && s.HasContent))
                    plan.Add(new ContractCoverageAudit.PlannedSection(
                        (section.Heading ?? "").Trim(), section.Covers, true));
            }

            foreach (var section in outline.Sections)
            {
                var heading = section.Heading.Trim();
                plan.Add(new ContractCoverageAudit.PlannedSection(
                    heading, section.Covers, byHeading.TryGetValue(heading, out var has) && has));
            }

            var text = string.Join("\n", written.SelectMany(s => s.Paragraphs.Concat(s.Items)));

            return ContractCoverageAudit.Measure(ledger, plan, text);
        }

        /// <summary>
        /// Keeps the plan's headings and order.
        ///
        /// A section the model renamed is still the section the plan asked for —
        /// matched by position when the name no longer matches — and one it invented
        /// is dropped, because a heading nothing planned covers nothing.
        /// </summary>
        private static List<ContractSectionContent> Align(
            IReadOnlyList<ContractOutline.PlannedSection> batch,
            List<ContractSectionContent> returned)
        {
            var aligned = new List<ContractSectionContent>();

            for (var i = 0; i < batch.Count; i++)
            {
                var planned = batch[i];

                // By name first. Positionally only when the model returned exactly
                // the sections it was asked for — a shorter list means it dropped
                // one, and pairing what is left by index would put the wrong text
                // under the wrong heading, which is worse than a missing section
                // the audit can report.
                var match = returned.FirstOrDefault(s =>
                    string.Equals(s.Heading?.Trim(), planned.Heading.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (returned.Count == batch.Count ? returned[i] : null);

                if (match is null || !match.HasContent) continue;

                match.Heading = planned.Heading;
                aligned.Add(match);
            }

            return aligned;
        }
    }

    /// <summary>
    /// What one generation run produced, and how well it did.
    /// </summary>
    public sealed record ContractGenerationOutcome(
        GeneratedContractContent Content,
        ContractCoverageLedger Ledger,
        ContractCoverageAudit Audit,
        ContractGenerationTelemetry Telemetry);

    /// <summary>
    /// The measurements of one run, for the log and for the record beside the
    /// draft.
    ///
    /// Deliberately all numbers and no text: this is written to the platform log
    /// and stored on the draft, and neither is a place for contract wording,
    /// customer data or anything from the provider's response.
    /// </summary>
    public sealed class ContractGenerationTelemetry
    {
        public string PromptVersion { get; set; } = "";
        public int LedgerItems { get; set; }
        public int SourceTopics { get; set; }
        public bool SourceAnalysisFailed { get; set; }
        public int PlannedSections { get; set; }
        public int UnplannedEntries { get; set; }
        public int WrittenSections { get; set; }
        public int ModelCalls { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TruncatedCalls { get; set; }
        public bool Repaired { get; set; }

        /// <summary>
        /// Adds one call to the tally, safely.
        ///
        /// The section calls run together now, and <c>count++</c> from several
        /// threads loses some of them — which would understate the very thing
        /// these numbers exist to measure.
        /// </summary>
        public void Record(AiCompletion answer)
        {
            lock (_tally)
            {
                ModelCalls++;
                InputTokens += answer.InputTokens ?? 0;
                OutputTokens += answer.OutputTokens ?? 0;

                if (answer.IsTruncated) TruncatedCalls++;
            }
        }

        private readonly object _tally = new();
        public double CoverageRatio { get; set; }
        public int CriticalGaps { get; set; }
        public int ElapsedMs { get; set; }

        public object ToRecord() => new
        {
            promptVersion = PromptVersion,
            ledgerItems = LedgerItems,
            sourceTopics = SourceTopics,
            sourceAnalysisFailed = SourceAnalysisFailed,
            plannedSections = PlannedSections,
            unplannedEntries = UnplannedEntries,
            writtenSections = WrittenSections,
            modelCalls = ModelCalls,
            inputTokens = InputTokens,
            outputTokens = OutputTokens,
            truncatedCalls = TruncatedCalls,
            repaired = Repaired,
            coverageRatio = Math.Round(CoverageRatio, 4),
            criticalGaps = CriticalGaps,
            elapsedMs = ElapsedMs
        };
    }
}

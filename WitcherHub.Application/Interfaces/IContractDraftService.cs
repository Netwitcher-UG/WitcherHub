using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Domain.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Generates and versions the contract text produced from confirmed positions.
    ///
    /// The positions remain the record of what was agreed; a draft is wording built
    /// from them. Drafts accumulate so they can be compared, and an approved draft
    /// is never replaced silently.
    /// </summary>
    public interface IContractDraftService
    {
        Task<ContractDraftResult> GenerateAsync(
            Guid contractId,
            GenerateDraftOptions options,
            CancellationToken ct = default);

        Task<IReadOnlyList<ContractDraftSummary>> GetDraftsAsync(Guid contractId, CancellationToken ct = default);

        Task<ContractDraftSummary?> GetDraftAsync(Guid contractId, int version, CancellationToken ct = default);

        /// <summary>
        /// Stores contract text that came from outside the system — pasted in, or
        /// lifted from a document the customer supplied — as a new version.
        ///
        /// The text is stored exactly as given. Nothing rewrites it, and a
        /// contract built this way needs no positions at all: the wording is what
        /// was agreed. Improving or restructuring it is a separate, explicit act.
        /// </summary>
        Task<ContractDraftResult> ImportTextAsync(
            Guid contractId,
            string documentText,
            string source,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a draft's text with wording a person edited by hand.
        /// </summary>
        Task<ContractDraftResult> SaveEditedAsync(
            Guid contractId, int version, string documentMarkdown, CancellationToken ct = default);

        /// <summary>
        /// Makes a version the active approved wording. A version already
        /// approved is superseded rather than erased and stays in the history.
        ///
        /// Approving over an existing approval is the act that changes which text
        /// is active, so <paramref name="confirmReplacingApproved"/> is required
        /// for it. Producing a version needs no such confirmation: it appends.
        /// </summary>
        Task<ContractDraftResult> ApproveAsync(
            Guid contractId,
            int version,
            Guid? approvedById,
            bool confirmReplacingApproved = false,
            CancellationToken ct = default);

        /// <summary>
        /// What this contract is built from: positions, supplied text, or both.
        ///
        /// Every layer that needs to know whether the contract can proceed asks
        /// this rather than counting positions. That is the whole point — the
        /// count was the bug.
        /// </summary>
        Task<ContractSource> GetSourceAsync(Guid contractId, CancellationToken ct = default);

        /// <summary>
        /// Where the contract stands: whether the source has been analysed,
        /// whether the values have been confirmed, whether a draft has been
        /// prepared, and what it costs at contract level.
        ///
        /// Read from stored state rather than worked out from a position count
        /// and a version number, which could not tell the original apart from
        /// the analysis or from the version ready to sign.
        /// </summary>
        Task<ContractWorkflowState> GetStateAsync(Guid contractId, CancellationToken ct = default);

        /// <summary>
        /// Reads a stored supplied version and records what it says, without
        /// changing a character of it. Optional: a contract can be prepared,
        /// approved and signed from supplied text that was never analysed.
        /// </summary>
        Task<ContractAnalysisResult> AnalyzeAsync(
            Guid contractId, int version, CancellationToken ct = default);

        /// <summary>
        /// Begins an analysis and returns at once, without waiting for it.
        ///
        /// <see cref="AnalyzeAsync"/> runs the model call inline, which is fine
        /// for a short document and wrong for a real contract: reading one takes
        /// longer than a platform proxy will hold a connection open, so the
        /// browser was shown HTTP 502 while the work was still running — and the
        /// work then finished into a request nobody was listening to.
        ///
        /// The reading is the same; only who waits for it changes. The caller
        /// polls <see cref="GetAnalysisProgressAsync"/>.
        /// </summary>
        Task<ContractAnalysisStart> StartAnalysisAsync(
            Guid contractId, int version, CancellationToken ct = default);

        /// <summary>
        /// How the analysis of this version is getting on: still running,
        /// finished, or failed with a reason worth showing.
        /// </summary>
        Task<ContractAnalysisProgress> GetAnalysisProgressAsync(
            Guid contractId, int version, CancellationToken ct = default);

        /// <summary>The extraction stored against a version, if it has one.</summary>
        Task<ContractExtractionDto?> GetExtractionAsync(
            Guid contractId, int version, CancellationToken ct = default);

        /// <summary>
        /// What the financial engine made of the last reading of this version:
        /// committed money separated from estimated, variable and optional, and
        /// every amount it would not total listed with the reason.
        ///
        /// Null for a version analysed before the semantic pipeline existed, or
        /// never analysed at all. A caller must treat that as "not known" rather
        /// than as zero.
        /// </summary>
        Task<Domain.Commercial.ContractFinancials?> GetFinancialsAsync(
            Guid contractId, int version, CancellationToken ct = default);

        /// <summary>
        /// Stores the extracted values a person has reviewed, and promotes the
        /// confirmed commercial facts onto the contract. Only what is confirmed
        /// is promoted; the rest stays a reading of the document.
        /// </summary>
        Task<ContractDraftResult> ConfirmExtractionAsync(
            Guid contractId, int version, ContractExtractionDto confirmed, CancellationToken ct = default);
    }

    public sealed class GenerateDraftOptions
    {
        /// <summary>Extra guidance for the wording. Never a source of commercial facts.</summary>
        public string? AdditionalInstructions { get; set; }

        public string Language { get; set; } = "de-DE";

        /// <summary>
        /// Party replacements a person has already accepted in the review screen.
        /// Anything not listed here is left alone — a party name is not changed
        /// on a guess.
        /// </summary>
        public IReadOnlyList<ConfirmedPartyReplacement> ConfirmedReplacements { get; set; } =
            Array.Empty<ConfirmedPartyReplacement>();

        /// <summary>
        /// Identifies one preparation request. A repeat carrying the same key
        /// returns the version the first one produced, so a double click or a
        /// retry after a timeout cannot leave two drafts behind.
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>Where a contract stands, as stored.</summary>
    public sealed record ContractWorkflowState(
        ContractSourceState SourceState,
        ContractReviewState ReviewState,
        ContractPreparationState PreparationState,
        ContractMoneyDto Money);

    /// <summary>
    /// What the contract says it costs, at contract level.
    ///
    /// <paramref name="AgreedTotalNet"/> null means no total was agreed, which is
    /// not the same as a total of zero — showing 0,00 € for a contract that names
    /// a price the system simply has not been told is worse than saying nothing.
    /// </summary>
    public sealed record ContractMoneyDto(
        decimal? AgreedTotalNet,
        decimal? VatRatePercent,
        string Currency,
        bool PriceDeliberatelyUnspecified);

    /// <summary>A replacement the user looked at and accepted.</summary>
    public sealed record ConfirmedPartyReplacement(string Field, string OldValue, string NewValue);

    /// <summary>What happened when an analysis was asked for.</summary>
    public sealed class ContractAnalysisStart
    {
        /// <summary>True when a reading is now running, or was already running.</summary>
        public bool Running { get; init; }

        /// <summary>
        /// True when this request found one already in flight and joined it
        /// rather than starting a second. Pressing the button twice costs one
        /// reading, not two.
        /// </summary>
        public bool AlreadyRunning { get; init; }

        /// <summary>Why it could not be started at all.</summary>
        public string? FailureReason { get; init; }

        public static ContractAnalysisStart Started() => new() { Running = true };

        public static ContractAnalysisStart Joined() =>
            new() { Running = true, AlreadyRunning = true };

        public static ContractAnalysisStart Refused(string reason) =>
            new() { Running = false, FailureReason = reason };
    }

    /// <summary>Where an analysis has got to.</summary>
    public sealed class ContractAnalysisProgress
    {
        public ContractExtractionStatus Status { get; init; }

        /// <summary>True while the reading is still going.</summary>
        public bool Running => Status == ContractExtractionStatus.Analysing;

        /// <summary>True once there is something to show.</summary>
        public bool Finished =>
            Status is ContractExtractionStatus.Analysed or ContractExtractionStatus.Confirmed;

        public bool Failed => Status == ContractExtractionStatus.Failed;

        /// <summary>Why it failed, in the words the user should read.</summary>
        public string? FailureReason { get; init; }

        /// <summary>True when trying again is worth offering.</summary>
        public bool IsTransientFailure { get; init; }

        /// <summary>The reading itself, once there is one.</summary>
        public ContractExtractionDto? Extraction { get; init; }

        /// <summary>How long the current reading has been going, for the screen.</summary>
        public TimeSpan? Elapsed { get; init; }
    }

    public sealed class ContractDraftResult
    {
        public bool Succeeded { get; init; }
        public string? FailureReason { get; init; }

        /// <summary>
        /// True when generation failed for a reason that may pass — the caller keeps
        /// the positions and lets the user write the contract by hand instead.
        /// </summary>
        public bool IsTransientFailure { get; init; }

        /// <summary>
        /// Set when the caller must confirm something before the operation can go
        /// ahead — approving over an existing approval, or editing a version that
        /// is immutable.
        /// </summary>
        public bool RequiresOverwriteConfirmation { get; init; }

        public ContractDraftSummary? Draft { get; init; }

        /// <summary>
        /// True when a repeated request returned the version the first one made
        /// instead of making another. The caller reports it as done, not as done
        /// twice.
        /// </summary>
        public bool WasAlreadyPrepared { get; init; }

        /// <summary>
        /// True when the contract was composed from the record without a model,
        /// because the assistant could not be used.
        ///
        /// Succeeding is the point — the work must not stop because OpenAI is
        /// down — but the result is plainer than a generated one, so the screen
        /// says so and offers to regenerate rather than letting the user assume
        /// this is the best the system can do.
        /// </summary>
        public bool ComposedWithoutAi { get; init; }

        /// <summary>How many reviewed values a person ticked, for the message.</summary>
        public int ConfirmedFieldCount { get; init; }

        /// <summary>How many the document actually stated, for the same message.</summary>
        public int StatedFieldCount { get; init; }

        /// <summary>
        /// Fields where the document said one thing and the record already said
        /// another, so the record was kept.
        ///
        /// Reported rather than applied silently: the user ticked those values,
        /// and being told "confirmed" while nothing changed would be a lie. They
        /// remain visible in the reading, and changing the contract is done on
        /// the contract.
        /// </summary>
        public IReadOnlyList<string> KeptFromRecord { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Fields the project had no answer for that the contract has now filled.
        /// </summary>
        public IReadOnlyList<string> FilledOnProject { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The contract-level figures as they stand after the operation, read
        /// back from the database. The screen refreshes from these rather than
        /// from what it sent, so what it shows is what was stored.
        /// </summary>
        public ContractMoneyDto? Money { get; init; }

        /// <summary>
        /// Things the generated contract does not account for, in the reviewer's
        /// words.
        ///
        /// Generation used to be silent about its own gaps: whatever came back was
        /// saved, and a contract missing most of the agreed scope looked exactly
        /// like one that had covered everything. Every version is now measured
        /// against the list of things it had to cover, and what it missed is said
        /// out loud before anybody approves it.
        ///
        /// Never ids — those are internal. Never a claim that something is wrong
        /// with the customer's data; only that a point is not stated and should be
        /// checked.
        /// </summary>
        public IReadOnlyList<string> ReviewNotes { get; init; } = Array.Empty<string>();

        public static ContractDraftResult Failed(string reason, bool transient = false) =>
            new() { Succeeded = false, FailureReason = reason, IsTransientFailure = transient };

        public static ContractDraftResult NeedsConfirmation(string reason) =>
            new() { Succeeded = false, FailureReason = reason, RequiresOverwriteConfirmation = true };
    }

    public sealed record ContractDraftSummary(
        Guid Id,
        int Version,
        string DocumentMarkdown,
        string? Model,
        string? PromptVersion,
        string? TemplateVersion,
        string? GeneratedBy,
        DateTimeOffset GeneratedAt,
        bool IsApproved,
        DateTimeOffset? ApprovedAt,
        string? DocumentHash,
        PositionTotalsDto? Totals)
    {
        /// <summary>Generated wording, a supplied source document, or a human edit.</summary>
        public ContractDraftKind Kind { get; init; } = ContractDraftKind.Generated;

        /// <summary>True for the untouchable original of a supplied document.</summary>
        public bool IsImmutableSource { get; init; }

        public string? SourceLanguage { get; init; }

        public ContractExtractionStatus ExtractionStatus { get; init; } = ContractExtractionStatus.NotAnalysed;

        /// <summary>Where the version stands, as distinct from what kind it is.</summary>
        public ContractDraftStatus Status { get; init; } = ContractDraftStatus.Draft;

        public bool IsSupplied => Kind is ContractDraftKind.Supplied;

        /// <summary>What kind of version this is, in words, for the version list.</summary>
        public string KindLabel => Kind switch
        {
            ContractDraftKind.Supplied => "Supplied source",
            ContractDraftKind.Prepared => "Prepared draft",
            ContractDraftKind.HumanEdited => "Manually edited draft",
            _ => "AI-generated draft"
        };

        public string StatusLabel => Status switch
        {
            ContractDraftStatus.Approved => "Approved",
            ContractDraftStatus.Superseded => "Superseded",
            ContractDraftStatus.PendingReview => "Pending review",
            ContractDraftStatus.Signed => "Signed",
            _ => "Draft"
        };
    }
}

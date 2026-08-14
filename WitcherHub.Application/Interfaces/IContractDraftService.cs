using WitcherHub.Application.Models.DTO.Contracts;

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
        /// Marks a version as the approved wording and freezes it. Approval is what
        /// makes the document eligible to be sent for signature.
        /// </summary>
        Task<ContractDraftResult> ApproveAsync(
            Guid contractId, int version, Guid? approvedById, CancellationToken ct = default);
    }

    public sealed class GenerateDraftOptions
    {
        /// <summary>Extra guidance for the wording. Never a source of commercial facts.</summary>
        public string? AdditionalInstructions { get; set; }

        public string Language { get; set; } = "de-DE";

        /// <summary>
        /// Required to replace wording that has already been approved. Without it,
        /// generating over an approved draft is refused.
        /// </summary>
        public bool OverwriteApproved { get; set; }
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

        /// <summary>Set when generation was refused because an approved draft exists.</summary>
        public bool RequiresOverwriteConfirmation { get; init; }

        public ContractDraftSummary? Draft { get; init; }

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
        PositionTotalsDto? Totals);
}

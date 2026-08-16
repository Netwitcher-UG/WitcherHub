using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Domain.Commercial;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Reads an arbitrary commercial document into structured terms.
    ///
    /// Reading only. It does not rewrite the document, does not save anything,
    /// and does not decide anything: what comes back is a proposal that a person
    /// confirms, corrects or rejects. It also does no arithmetic — the totals on
    /// the result are the application's, calculated deterministically from the
    /// components the analyser identified.
    /// </summary>
    public interface ISemanticContractAnalyzer
    {
        Task<SemanticAnalysisResult> AnalyzeAsync(
            string documentText,
            SemanticAnalysisOptions? options = null,
            CancellationToken ct = default);
    }

    public sealed class SemanticAnalysisOptions
    {
        /// <summary>
        /// What language the document is expected to be in. A hint only — the
        /// analyser is told to verify it, because a wrong hint must not become a
        /// wrong reading.
        /// </summary>
        public string? LanguageHint { get; set; }

        /// <summary>
        /// The currency to assume where a document names amounts without a
        /// currency. Used only to label figures that already exist; it never
        /// creates one.
        /// </summary>
        public string FallbackCurrency { get; set; } = "EUR";

        /// <summary>
        /// The contract's length in months, where it is known from outside the
        /// document. Lets a recurring charge with no stated end be totalled
        /// against the contract's own term rather than left uncalculable.
        /// </summary>
        public int? ContractMonths { get; set; }
    }

    public sealed class SemanticAnalysisResult
    {
        public bool Succeeded { get; init; }
        public string? FailureReason { get; init; }
        public bool IsTransientFailure { get; init; }
        public string? CorrelationId { get; init; }

        /// <summary>Everything recognised, including what is not a charge.</summary>
        public SemanticExtractionDto? Extraction { get; init; }

        /// <summary>The charges, in the domain's own model, after validation.</summary>
        public IReadOnlyList<CommercialTerm> Terms { get; init; } = Array.Empty<CommercialTerm>();

        /// <summary>What a person needs to look at. Terms with issues are kept.</summary>
        public IReadOnlyList<TermIssue> Issues { get; init; } = Array.Empty<TermIssue>();

        /// <summary>Proposals with nothing usable in them at all, and why.</summary>
        public IReadOnlyList<string> DiscardedReasons { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The money, calculated by the application. Committed amounts are
        /// separated from estimated, variable and optional ones, and anything
        /// that could not be resolved is listed with its reason.
        /// </summary>
        public ContractFinancials? Financials { get; init; }

        public string? Model { get; init; }
        public string? PromptVersion { get; init; }

        public static SemanticAnalysisResult Failed(
            string reason, bool transient = false, string? correlationId = null) =>
            new()
            {
                Succeeded = false,
                FailureReason = reason,
                IsTransientFailure = transient,
                CorrelationId = correlationId
            };
    }
}

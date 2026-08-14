using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Reads a supplied contract and reports what it says.
    ///
    /// Reading only. It does not rewrite the document, does not save anything,
    /// and does not decide anything: the result is a proposal that a person
    /// confirms or corrects. Separating this from wording generation is the point
    /// — analysis that also rewrites is analysis you cannot check.
    /// </summary>
    public interface IContractTextAnalyzer
    {
        Task<ContractAnalysisResult> AnalyzeAsync(
            string documentText,
            string? languageHint = null,
            CancellationToken ct = default);
    }

    public sealed class ContractAnalysisResult
    {
        public bool Succeeded { get; init; }
        public ContractExtractionDto? Extraction { get; init; }
        public string? FailureReason { get; init; }
        public bool IsTransientFailure { get; init; }

        /// <summary>The reference printed in the log next to the technical detail.</summary>
        public string? CorrelationId { get; init; }

        public string? Model { get; init; }

        public static ContractAnalysisResult Failed(
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

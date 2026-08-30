using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Rewriting a contract from wording a person edited by hand.
    ///
    /// This lived in the override page's model, which meant the model call
    /// happened on the request that asked for it. A contract takes longer to write
    /// than a platform proxy will hold a connection open, so that request came
    /// back HTTP 502 while the work was still going, and the finished document
    /// landed in a request nobody was listening to — the same fault that took the
    /// positions screen off the request thread, still standing on this one.
    ///
    /// Lifting it here lets the background job do the work, and leaves the page
    /// with nothing to do but collect the form and poll.
    /// </summary>
    public interface IContractOverrideGenerator
    {
        Task<ContractOverrideGenerationResult> GenerateAsync(
            Guid contractId,
            ContractStructuredTermsDto structured,
            string? userId,
            CancellationToken ct = default);
    }

    public sealed class ContractOverrideGenerationResult
    {
        public bool Succeeded { get; init; }

        /// <summary>Shown to the user as it stands. Never carries provider detail.</summary>
        public string? FailureReason { get; init; }

        /// <summary>Whether pressing the button again could plausibly work.</summary>
        public bool IsTransientFailure { get; init; }

        /// <summary>Where the page should go once the contract has been written.</summary>
        public Guid ProjectId { get; init; }

        public static ContractOverrideGenerationResult Ok(Guid projectId) =>
            new() { Succeeded = true, ProjectId = projectId };

        public static ContractOverrideGenerationResult Fail(string reason, bool transient) =>
            new() { Succeeded = false, FailureReason = reason, IsTransientFailure = transient };
    }
}

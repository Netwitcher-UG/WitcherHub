using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Rewriting a contract from wording a person edited by hand.
    ///
    /// This lived in the override page's model and ran on the POST that carried
    /// the form. That request was not actually slow — the screen always supplies
    /// StructuredOverride, which is the branch of ContractDocumentGenerator that
    /// composes from the edited structure and never calls the model — but nothing
    /// about the screen said so: no progress while it ran, no guard against a
    /// second press, and no way to report a failure except replacing the screen
    /// with an error page. It is also one null away from the model branch.
    ///
    /// Lifting it here lets the background job own the work, so the page has
    /// nothing to do but collect the form and poll, and reports success and
    /// failure the same way the other two assistant actions already do.
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

using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Interfaces
{
    /// <summary>
    /// Turns rough notes about services into structured contract positions.
    ///
    /// Kept separate from contract-document generation: organising positions and
    /// writing contract prose are different requests with different prompts, and
    /// mixing them would make it impossible to tell which step invented what.
    /// </summary>
    public interface IAiPositionOrganizer
    {
        Task<OrganizePositionsResult> OrganizeAsync(
            OrganizePositionsRequest request,
            CancellationToken ct = default);
    }

    public sealed class OrganizePositionsRequest
    {
        /// <summary>Free text describing the work, as typed by the user.</summary>
        public string RoughInput { get; set; } = "";

        /// <summary>
        /// Positions the user has already entered. Their commercial fields are
        /// authoritative and the result is checked against them.
        /// </summary>
        public List<ManualPositionDto> ExistingPositions { get; set; } = new();

        public string Currency { get; set; } = "EUR";
        public string Language { get; set; } = "de-DE";
    }

    /// <summary>
    /// The organiser's proposal. Nothing here is applied until a person confirms it.
    /// </summary>
    public sealed class OrganizePositionsResult
    {
        public bool Succeeded { get; init; }

        /// <summary>Why the attempt failed, in words a user can act on.</summary>
        public string? FailureReason { get; init; }

        /// <summary>
        /// True when the model could not be reached or its answer was unusable. The
        /// caller keeps the user's positions untouched in that case.
        /// </summary>
        public bool IsTransientFailure { get; init; }

        public List<ManualPositionDto> Positions { get; init; } = new();

        /// <summary>
        /// Differences between what the user supplied and what came back, so the
        /// user reviews changes rather than discovering them later.
        /// </summary>
        public List<PositionChange> Changes { get; init; } = new();

        /// <summary>
        /// Commercial values the model tried to alter. These are reverted to the
        /// user's figures before the result is returned; the list exists so the
        /// attempt is visible rather than silent.
        /// </summary>
        public List<PositionChange> RejectedChanges { get; init; } = new();

        public string? Model { get; init; }
        public string? PromptVersion { get; init; }

        public static OrganizePositionsResult Failed(string reason, bool transient = false) =>
            new() { Succeeded = false, FailureReason = reason, IsTransientFailure = transient };
    }

    /// <summary>
    /// One field the organiser changed, added or attempted to change.
    /// </summary>
    public sealed record PositionChange(
        string PositionTitle,
        string Field,
        string? Before,
        string? After,
        PositionChangeKind Kind);

    public enum PositionChangeKind
    {
        /// <summary>Wording improved or a descriptive field filled in. Safe.</summary>
        Descriptive = 0,

        /// <summary>A whole position the model proposed that the user did not enter.</summary>
        AddedPosition = 1,

        /// <summary>A commercial value the model tried to change. Reverted.</summary>
        RejectedCommercial = 2
    }
}

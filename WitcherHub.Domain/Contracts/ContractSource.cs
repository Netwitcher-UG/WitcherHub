using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Domain.Contracts
{
    /// <summary>
    /// Whether a contract has enough behind it to be turned into a document.
    ///
    /// This is the whole rule, and it lives here so that there is exactly one of
    /// it. The rule used to be "at least one position", repeated in the page, the
    /// browser, the draft service, the position store, the details page and the
    /// signing page. A contract whose wording is a document the customer supplied
    /// has no positions and never will, so six copies of a position count all
    /// refused it — and each copy had to be found separately to fix it.
    ///
    /// The rule is now: a contract may proceed on valid positions, or on supplied
    /// contract text, or on both. It is blocked only when it has neither.
    /// </summary>
    public readonly record struct ContractSource
    {
        private ContractSource(bool hasPositions, bool hasSuppliedText, bool hasApprovedText)
        {
            HasPositions = hasPositions;
            HasSuppliedText = hasSuppliedText;
            HasApprovedText = hasApprovedText;
        }

        /// <summary>At least one position the user has saved.</summary>
        public bool HasPositions { get; }

        /// <summary>At least one stored version of contract text.</summary>
        public bool HasSuppliedText { get; }

        /// <summary>One of those versions has been approved.</summary>
        public bool HasApprovedText { get; }

        public static ContractSource From(int positionCount, bool hasSuppliedText, bool hasApprovedText = false) =>
            new(positionCount > 0, hasSuppliedText || hasApprovedText, hasApprovedText);

        /// <summary>
        /// The rule. Everything else on this type is presentation.
        /// </summary>
        public bool CanGenerate => HasPositions || HasSuppliedText;

        /// <summary>
        /// What the contract is actually built from, for storing on the contract
        /// and for telling the user which of the three paths they are on.
        /// </summary>
        public ContractSourceMode Mode =>
            HasPositions && HasSuppliedText ? ContractSourceMode.Hybrid
            : HasSuppliedText ? ContractSourceMode.SuppliedText
            : ContractSourceMode.Positions;

        /// <summary>
        /// Why generation is refused, or null when it is allowed. A caller that
        /// needs a message should use this rather than writing its own, so the
        /// wording cannot drift between the browser and the server.
        /// </summary>
        public string? BlockingReason =>
            CanGenerate
                ? null
                : "This contract has neither positions nor contract text. Add a position, or paste the contract text, and it can be generated from either.";

        /// <summary>The label for the primary action, which follows the source.</summary>
        public string PrimaryActionLabel => Mode switch
        {
            ContractSourceMode.SuppliedText => "Prepare supplied contract",
            ContractSourceMode.Hybrid => "Generate from text and positions",
            _ => "Generate from positions"
        };

        public string ModeLabel => Mode switch
        {
            ContractSourceMode.SuppliedText => "Supplied text",
            ContractSourceMode.Hybrid => "Positions and supplied text",
            _ => "Positions"
        };

        /// <summary>
        /// What to say where the position list is empty. With supplied text an
        /// empty list is a finished state, not a missing step, and saying "add a
        /// position first" there is what sent users round in circles.
        /// </summary>
        public string EmptyPositionsMessage =>
            HasSuppliedText
                ? "No positions were added. This contract will be generated from the supplied contract text. You may optionally add or extract positions."
                : "Add a saved service, create a manual position, or paste an existing contract. None of these is required by the others.";
    }
}

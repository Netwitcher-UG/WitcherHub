namespace WitcherHub.Application.Services.Contracts.Clauses
{
    /// <summary>
    /// How far a clause has got through legal review.
    ///
    /// The whole point of a clause library is that the legal wording stops being
    /// something a language model improvises per contract and becomes something a
    /// lawyer signs off once. That only holds if the sign-off is recorded, so
    /// this is stored on every module and checked before a contract can be
    /// approved.
    /// </summary>
    public enum ClauseReviewStatus
    {
        /// <summary>Drafted, not yet seen by a lawyer. Usable for drafts only.</summary>
        PendingLegalReview = 0,

        /// <summary>A German lawyer has reviewed and released this wording.</summary>
        Approved = 1,

        /// <summary>Withdrawn. Never enters a new contract.</summary>
        Retired = 2
    }

    /// <summary>
    /// What kind of obligation a clause belongs to, so that acceptance,
    /// warranty and term clauses are not attached to a contract that has no
    /// place for them.
    /// </summary>
    public enum ServiceNature
    {
        /// <summary>§§ 611 ff. BGB — effort is owed, no particular outcome.</summary>
        Service = 0,

        /// <summary>§§ 631 ff. BGB — a defined result, capable of acceptance.</summary>
        Work = 1,

        /// <summary>Both, in one contract.</summary>
        Mixed = 2
    }

    /// <summary>
    /// Whether the contract runs once or keeps running.
    /// </summary>
    public enum ServiceRecurrence
    {
        OneTime = 0,
        Recurring = 1,
        Mixed = 2
    }

    /// <summary>
    /// One releasable unit of contract wording.
    ///
    /// A module is not a string. It carries the conditions under which it may be
    /// used at all, the structured data it needs before its text is complete, the
    /// modules it must never appear beside, and its review state — because every
    /// one of those is a way a legally wrong contract gets assembled from legally
    /// right sentences.
    ///
    /// The text is fixed here rather than generated. The model may choose from
    /// these; it may not write them, and it may not alter what they mean.
    /// </summary>
    public sealed record ClauseModule
    {
        /// <summary>Stable identifier. Never reused for different wording.</summary>
        public required string Id { get; init; }

        /// <summary>
        /// Bumped whenever the text changes in a way that alters its meaning.
        /// Recorded on the contract version, so it stays answerable later which
        /// wording a customer actually signed.
        /// </summary>
        public required int Version { get; init; }

        /// <summary>The heading this clause appears under in the contract.</summary>
        public required string Title { get; init; }

        /// <summary>
        /// The clause itself, in German. Placeholders are written as
        /// <c>{{FieldName}}</c> and must all be satisfied from
        /// <see cref="RequiredFields"/> before the module can be rendered.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Where this clause sits in the finished document. Sections are ordered
        /// by this, so the contract reads in the same order every time regardless
        /// of the order modules were selected in.
        /// </summary>
        public required int SectionOrder { get; init; }

        /// <summary>
        /// The natures this clause makes sense for. An empty list means any.
        ///
        /// This is what keeps an Abnahme clause off a pure advisory contract:
        /// acceptance is a concept of Werkvertragsrecht, and putting it on a
        /// Dienstvertrag invents an obligation nobody agreed to.
        /// </summary>
        public IReadOnlyList<ServiceNature> AppliesToNatures { get; init; } = [];

        /// <summary>Recurrences this clause makes sense for. Empty means any.</summary>
        public IReadOnlyList<ServiceRecurrence> AppliesToRecurrences { get; init; } = [];

        /// <summary>
        /// Structured values the text needs. A module whose required fields are
        /// not all present is never rendered — it is reported as missing
        /// information instead, so the contract says "this has to be decided"
        /// rather than quietly carrying an invented number.
        /// </summary>
        public IReadOnlyList<string> RequiredFields { get; init; } = [];

        /// <summary>
        /// Modules that must not appear together. Two clauses can each be
        /// correct and still contradict each other — a simple licence beside an
        /// exclusive one, a fixed term beside a rolling one.
        /// </summary>
        public IReadOnlyList<string> IncompatibleWith { get; init; } = [];

        public ClauseReviewStatus ReviewStatus { get; init; } = ClauseReviewStatus.PendingLegalReview;

        /// <summary>From when this wording may be used.</summary>
        public DateOnly EffectiveFrom { get; init; } = new(2026, 1, 1);

        /// <summary>
        /// True when the module is selected unless somebody decides otherwise.
        ///
        /// Deliberately rare. A clause that quietly grants the provider a right
        /// the customer never discussed is exactly the kind of surprise German
        /// AGB law is unsympathetic to, so defaults are limited to the ones the
        /// owner has explicitly asked for.
        /// </summary>
        public bool DefaultSelected { get; init; }

        /// <summary>
        /// Why a human might need to look at this one, shown in the review
        /// screen. Null when the clause raises nothing unusual.
        /// </summary>
        public string? LegalNote { get; init; }

        /// <summary>The placeholders this text actually contains.</summary>
        public IReadOnlyList<string> Placeholders =>
            System.Text.RegularExpressions.Regex
                .Matches(Text, @"\{\{(?<name>[A-Za-z0-9_]+)\}\}")
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
    }
}

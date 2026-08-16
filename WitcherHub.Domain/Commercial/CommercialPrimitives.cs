namespace WitcherHub.Domain.Commercial
{
    /// <summary>
    /// How something repeats.
    ///
    /// Kept separate from the period itself because "every two weeks", "per API
    /// call" and "on each milestone" are all recurrence and only one of them has
    /// a period. The old model had a single BillingCycle enum whose members were
    /// OneTime, Monthly, Quarterly, SemiAnnual and Annual — which cannot express
    /// weekly, fortnightly, per-unit-consumed, or anything a future contract
    /// happens to use.
    /// </summary>
    public enum RecurrenceKind
    {
        /// <summary>Happens once.</summary>
        None = 0,

        /// <summary>Repeats on a calendar period: every N days/weeks/months/years.</summary>
        Periodic = 1,

        /// <summary>Happens per unit consumed, so the count is not known in advance.</summary>
        PerUsage = 2,

        /// <summary>Happens when a milestone is reached.</summary>
        PerMilestone = 3,

        /// <summary>Happens when some other stated condition is met.</summary>
        OnCondition = 4,

        /// <summary>The document does not say. Not the same as None.</summary>
        Unknown = 5
    }

    public enum PeriodUnit
    {
        Day = 0,
        Week = 1,
        Month = 2,
        Quarter = 3,
        Year = 4
    }

    /// <summary>
    /// A repetition, as a period and a multiplier.
    ///
    /// "Monthly" is (Periodic, Month, 1). "Every two weeks" is (Periodic, Week, 2).
    /// "Quarterly" is (Periodic, Quarter, 1) rather than a distinct enum member,
    /// so an interval nobody anticipated needs no code change.
    ///
    /// <see cref="SourcePhrase"/> keeps what the document actually said. The
    /// normalised value is what the system reasons with; the phrase is what lets
    /// a human check the normalisation was right.
    /// </summary>
    public sealed record Recurrence(
        RecurrenceKind Kind,
        PeriodUnit? Unit = null,
        int Interval = 1,
        string? Condition = null,
        string? SourcePhrase = null)
    {
        public static Recurrence Once(string? sourcePhrase = null) =>
            new(RecurrenceKind.None, SourcePhrase: sourcePhrase);

        public static Recurrence Unknown(string? sourcePhrase = null) =>
            new(RecurrenceKind.Unknown, SourcePhrase: sourcePhrase);

        public static Recurrence Every(PeriodUnit unit, int interval = 1, string? sourcePhrase = null) =>
            new(RecurrenceKind.Periodic, unit, Math.Max(1, interval), SourcePhrase: sourcePhrase);

        public static Recurrence PerUsage(string? condition = null, string? sourcePhrase = null) =>
            new(RecurrenceKind.PerUsage, Condition: condition, SourcePhrase: sourcePhrase);

        public static Recurrence PerMilestone(string? condition = null, string? sourcePhrase = null) =>
            new(RecurrenceKind.PerMilestone, Condition: condition, SourcePhrase: sourcePhrase);

        public static Recurrence OnCondition(string condition, string? sourcePhrase = null) =>
            new(RecurrenceKind.OnCondition, Condition: condition, SourcePhrase: sourcePhrase);

        public bool IsRecurring => Kind is not (RecurrenceKind.None or RecurrenceKind.Unknown);

        /// <summary>
        /// Whether the number of occurrences over a known span can be counted.
        /// Usage and condition based recurrences cannot, which is what keeps them
        /// out of a committed total.
        /// </summary>
        public bool IsCountable => Kind is RecurrenceKind.Periodic && Unit is not null;

        /// <summary>
        /// How many times this occurs across a span of whole months. Null when it
        /// cannot be counted — never a guess, and never zero standing in for
        /// "don't know".
        /// </summary>
        public decimal? OccurrencesInMonths(int months)
        {
            if (months <= 0) return null;
            if (Kind is RecurrenceKind.None) return 1m;
            if (!IsCountable) return null;

            var perMonth = Unit switch
            {
                PeriodUnit.Day => 30.436875m,          // mean Gregorian month
                PeriodUnit.Week => 4.348125m,
                PeriodUnit.Month => 1m,
                PeriodUnit.Quarter => 1m / 3m,
                PeriodUnit.Year => 1m / 12m,
                _ => (decimal?)null
            };

            if (perMonth is null) return null;

            return months * perMonth.Value / Interval;
        }
    }

    /// <summary>
    /// How much of an amount the customer is actually on the hook for.
    ///
    /// The distinction the old model had no way to express: a rate of 80 per hour
    /// for hours nobody has committed to is not the same money as 2,500 a month
    /// for twelve months, and adding the first into a contract total states a
    /// number the contract does not.
    /// </summary>
    public enum Commitment
    {
        /// <summary>Agreed and calculable. Only this reaches the committed total.</summary>
        Committed = 0,

        /// <summary>A figure given as an expectation, not a promise.</summary>
        Estimated = 1,

        /// <summary>Depends on something not fixed — usage, hours, volume.</summary>
        Variable = 2,

        /// <summary>Only charged if taken up.</summary>
        Optional = 3,

        /// <summary>Only charged if a stated condition occurs.</summary>
        Conditional = 4,

        /// <summary>Not determinable from what is known. Never treated as zero.</summary>
        Unknown = 5
    }

    /// <summary>
    /// An amount, its currency, and what kind of obligation it is.
    ///
    /// <see cref="Value"/> is nullable on purpose: a rate that the document does
    /// not state has to survive as "not stated" rather than as 0, because 0 is a
    /// price and "not stated" is not.
    /// </summary>
    public sealed record MoneyAmount(
        decimal? Value,
        string? Currency = null,
        Commitment Commitment = Commitment.Committed,
        string? SourcePhrase = null)
    {
        public static MoneyAmount NotStated(string? currency = null) =>
            new(null, currency, Commitment.Unknown);

        public bool HasValue => Value.HasValue;

        public bool CountsTowardsCommittedTotal => Commitment is Commitment.Committed && Value.HasValue;
    }

    /// <summary>
    /// How a price is arrived at.
    ///
    /// Extensible by design. <see cref="Custom"/> exists so that a pricing
    /// arrangement nobody anticipated is represented and reviewable rather than
    /// forced into the nearest member and quietly misstated.
    /// </summary>
    public enum PricingModelKind
    {
        /// <summary>One agreed amount for the whole thing.</summary>
        FixedAmount = 0,

        /// <summary>An amount per recurrence period.</summary>
        RecurringAmount = 1,

        /// <summary>An amount per unit of something countable.</summary>
        PerUnit = 2,

        /// <summary>Per unit of time worked.</summary>
        TimeAndMaterials = 3,

        /// <summary>Per unit consumed, counted after the fact.</summary>
        UsageBased = 4,

        /// <summary>Rate depends on which band the quantity falls in.</summary>
        Tiered = 5,

        /// <summary>A percentage of some other amount.</summary>
        Percentage = 6,

        /// <summary>Paid on reaching defined milestones.</summary>
        Milestone = 7,

        /// <summary>A reduction rather than a charge.</summary>
        Credit = 8,

        /// <summary>No charge.</summary>
        NoCharge = 9,

        /// <summary>
        /// Something the members above do not describe. The arrangement is kept
        /// in <c>CustomPricingModel</c> and the figures are kept as stated;
        /// nothing is inferred from it automatically.
        /// </summary>
        Custom = 10,

        /// <summary>The document does not make the pricing basis clear.</summary>
        Unknown = 11
    }

    /// <summary>
    /// What a detected passage actually is.
    ///
    /// Most sentences in a contract are not billable lines. Turning every one
    /// that mentions money into a position produces a total that is not the
    /// contract's total, so classification comes before structure.
    /// </summary>
    public enum CommercialConceptKind
    {
        /// <summary>Something billable. Only this becomes a position.</summary>
        BillablePosition = 0,

        ContractClause = 1,
        PaymentCondition = 2,
        ProjectRequirement = 3,
        ServiceDescription = 4,
        CustomerInformation = 5,
        CompanyInformation = 6,
        LegalClause = 7,
        CommercialCondition = 8,
        Deadline = 9,
        Deliverable = 10,
        ScopeLimitation = 11,
        OptionalItem = 12,
        ContextualInformation = 13,

        /// <summary>Recognised as meaningful but not classifiable. Kept for review.</summary>
        Unclassified = 14
    }

    /// <summary>
    /// Why a value is what it is, and how much to trust it.
    ///
    /// Carried alongside every extracted value rather than discarded after
    /// import, so a figure can always be traced back to the sentence it came
    /// from and the reason it was read that way.
    /// </summary>
    public sealed record Provenance(
        string? SourceSnippet = null,
        int? SourceOffset = null,
        double Confidence = 0d,
        string? Reasoning = null,
        bool IsAmbiguous = false,
        string? Ambiguity = null)
    {
        public static Provenance None => new();

        /// <summary>
        /// Set once a person has looked at the value. Regeneration must not
        /// overwrite what this marks.
        /// </summary>
        public bool HumanReviewed { get; init; }

        public Provenance Clamped() => this with { Confidence = Math.Clamp(Confidence, 0d, 1d) };
    }
}

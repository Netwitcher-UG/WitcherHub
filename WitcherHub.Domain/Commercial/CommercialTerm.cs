namespace WitcherHub.Domain.Commercial
{
    /// <summary>
    /// One period during which a term's pricing conditions hold.
    ///
    /// A term has zero phases when its pricing never changes, and as many as the
    /// agreement describes when it does. Nothing here assumes a phase is a month,
    /// or that phases are contiguous, or that there is a fixed number of them:
    /// a phase may be bounded by dates, by a count of billing periods, by a
    /// project stage, by a quantity, or by a condition expressed in words.
    /// </summary>
    public sealed record PricingPhase
    {
        /// <summary>What the agreement calls this phase, in its own words.</summary>
        public string? Label { get; init; }

        /// <summary>Order within the term. Phases need not be dated to be ordered.</summary>
        public int Sequence { get; init; }

        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }

        /// <summary>
        /// A boundary the agreement states in words rather than as a date — "after
        /// go-live", "once 10,000 orders are reached". Kept verbatim because
        /// turning it into a date would be inventing one.
        /// </summary>
        public string? StartCondition { get; init; }
        public string? EndCondition { get; init; }

        /// <summary>
        /// How long the phase lasts, counted in <see cref="DurationUnit"/>. Null
        /// when the agreement does not say.
        /// </summary>
        public int? DurationCount { get; init; }
        public PeriodUnit? DurationUnit { get; init; }

        public PricingModelKind PricingModel { get; init; } = PricingModelKind.Unknown;
        public string? CustomPricingModel { get; init; }

        public MoneyAmount Rate { get; init; } = MoneyAmount.NotStated();

        public decimal? Quantity { get; init; }
        public string? QuantityUnit { get; init; }

        /// <summary>How often the rate is charged during this phase.</summary>
        public Recurrence BillingRecurrence { get; init; } = Recurrence.Unknown();

        public decimal? DiscountPercent { get; init; }
        public MoneyAmount? DiscountAmount { get; init; }

        public string? Conditions { get; init; }

        public Provenance Provenance { get; init; } = Provenance.None;

        /// <summary>
        /// How many whole months the phase covers, when that is determinable.
        /// Null rather than a default, so an undated phase cannot silently
        /// contribute a made-up span to a total.
        /// </summary>
        public int? LengthInMonths()
        {
            if (DurationCount is { } count && DurationUnit is { } unit)
            {
                return unit switch
                {
                    PeriodUnit.Day => Math.Max(1, (int)Math.Round(count / 30.436875m)),
                    PeriodUnit.Week => Math.Max(1, (int)Math.Round(count / 4.348125m)),
                    PeriodUnit.Month => count,
                    PeriodUnit.Quarter => count * 3,
                    PeriodUnit.Year => count * 12,
                    _ => null
                };
            }

            if (StartDate is { } from && EndDate is { } to && to >= from)
            {
                var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
                if (to.Day >= from.Day) months += 1;
                return Math.Max(1, months);
            }

            return null;
        }
    }

    /// <summary>
    /// A commercial term: one thing the agreement says will be charged for.
    ///
    /// This is the generic replacement for a "position". It carries what the
    /// agreement means rather than the shape of any particular contract: the
    /// pricing basis, what is being counted, how often it is billed as distinct
    /// from how often it is delivered, over what span, in how many phases, and —
    /// crucially — how much of it is actually committed.
    ///
    /// Every field that the agreement may leave unsaid is nullable, and none of
    /// them defaults to a plausible value. A term the system cannot fully
    /// understand survives as a term it cannot fully understand.
    /// </summary>
    public sealed record CommercialTerm
    {
        /// <summary>Stable identity within an extraction or an edit session.</summary>
        public string Key { get; init; } = Guid.NewGuid().ToString("n");

        public string Name { get; init; } = "";
        public string? Description { get; init; }

        /// <summary>
        /// What kind of thing this is, in the agreement's own vocabulary. A free
        /// string rather than an enum: the set of things anybody might sell is
        /// not knowable in advance, and a fixed list would force every unfamiliar
        /// service into "Other".
        /// </summary>
        public string? Category { get; init; }

        public PricingModelKind PricingModel { get; init; } = PricingModelKind.Unknown;

        /// <summary>
        /// The arrangement in words, when <see cref="PricingModel"/> is
        /// <see cref="PricingModelKind.Custom"/>. Preserved rather than
        /// approximated.
        /// </summary>
        public string? CustomPricingModel { get; init; }

        public decimal? Quantity { get; init; }

        /// <summary>
        /// What the quantity counts — "hours", "Shops", "Bestellungen". Free text:
        /// this is the unit the agreement uses, and it is not the same thing as
        /// the billing period.
        /// </summary>
        public string? QuantityUnit { get; init; }

        public MoneyAmount UnitRate { get; init; } = MoneyAmount.NotStated();

        /// <summary>
        /// A single agreed amount for the whole term, where the agreement gives
        /// one instead of a rate and a quantity.
        /// </summary>
        public MoneyAmount? FixedAmount { get; init; }

        /// <summary>
        /// How often it is billed. Deliberately separate from
        /// <see cref="DeliveryRecurrence"/>: work delivered weekly may be billed
        /// monthly, and collapsing the two loses the difference.
        /// </summary>
        public Recurrence BillingRecurrence { get; init; } = Recurrence.Unknown();

        /// <summary>How often it is delivered or performed.</summary>
        public Recurrence DeliveryRecurrence { get; init; } = Recurrence.Unknown();

        /// <summary>When money changes hands, which is a third thing again.</summary>
        public string? PaymentSchedule { get; init; }

        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }

        /// <summary>How long the term runs, when stated as a length rather than dates.</summary>
        public int? DurationCount { get; init; }
        public PeriodUnit? DurationUnit { get; init; }

        /// <summary>
        /// Pricing periods within the term. Empty when the price never changes;
        /// the count is whatever the agreement describes.
        /// </summary>
        public IReadOnlyList<PricingPhase> Phases { get; init; } = Array.Empty<PricingPhase>();

        /// <summary>A floor the customer owes regardless of usage.</summary>
        public MoneyAmount? MinimumCommitment { get; init; }

        /// <summary>A ceiling beyond which no further charge arises.</summary>
        public MoneyAmount? Cap { get; init; }

        public decimal? DiscountPercent { get; init; }
        public MoneyAmount? DiscountAmount { get; init; }

        /// <summary>
        /// Tax rate as a percentage, null when the agreement does not state one.
        /// "Plus statutory VAT" states a treatment without stating a rate, which
        /// is <see cref="TaxTreatment"/> with a null rate rather than a guess.
        /// </summary>
        public decimal? TaxRatePercent { get; init; }
        public string? TaxTreatment { get; init; }

        /// <summary>False when the charge only arises if the customer takes it up.</summary>
        public bool IsMandatory { get; init; } = true;

        /// <summary>What has to happen for the charge to arise at all.</summary>
        public string? Conditions { get; init; }

        public string? Notes { get; init; }

        /// <summary>
        /// How firm the money is. Set from what the agreement says, not from
        /// whether a number happens to be present: a stated hourly rate with no
        /// committed hours is <see cref="Commitment.Variable"/> even though the
        /// rate is perfectly definite.
        /// </summary>
        public Commitment Commitment { get; init; } = Commitment.Unknown;

        public Provenance Provenance { get; init; } = Provenance.None;

        /// <summary>
        /// True once a person has edited or accepted this term. Regeneration
        /// leaves these alone unless the user asks for them to be replaced.
        /// </summary>
        public bool IsHumanReviewed { get; init; }

        /// <summary>Things a person needs to resolve before this is dependable.</summary>
        public IReadOnlyList<string> OpenQuestions { get; init; } = Array.Empty<string>();

        public bool HasPhases => Phases.Count > 0;

        /// <summary>
        /// The term's span in whole months, from a duration, from dates, or from
        /// its phases. Null when none of those is stated — which is the answer
        /// that keeps an unbounded recurring charge out of a committed total.
        /// </summary>
        public int? LengthInMonths()
        {
            if (DurationCount is { } count && DurationUnit is { } unit)
            {
                return unit switch
                {
                    PeriodUnit.Day => Math.Max(1, (int)Math.Round(count / 30.436875m)),
                    PeriodUnit.Week => Math.Max(1, (int)Math.Round(count / 4.348125m)),
                    PeriodUnit.Month => count,
                    PeriodUnit.Quarter => count * 3,
                    PeriodUnit.Year => count * 12,
                    _ => null
                };
            }

            if (StartDate is { } from && EndDate is { } to && to >= from)
            {
                var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
                if (to.Day >= from.Day) months += 1;
                return Math.Max(1, months);
            }

            if (HasPhases)
            {
                var lengths = Phases.Select(p => p.LengthInMonths()).ToList();
                if (lengths.All(l => l.HasValue))
                    return lengths.Sum(l => l!.Value);
            }

            return null;
        }
    }
}

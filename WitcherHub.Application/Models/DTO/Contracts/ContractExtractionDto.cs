using System.Text.Json.Serialization;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    /// <summary>
    /// One value read out of a supplied contract.
    ///
    /// A bare value would not be safe to act on. A contract is a commercial and
    /// legal document, and a figure a model believed it saw is a different thing
    /// from a figure a person agreed — so every value carries where it was read,
    /// how sure the reader was, and whether a person still has to confirm it.
    /// Nothing here becomes contract data until <see cref="Confirmed"/> is set by
    /// a person.
    /// </summary>
    public sealed class ExtractedValue
    {
        public string? Value { get; set; }

        /// <summary>The passage it was read from, quoted from the source document.</summary>
        public string? SourceText { get; set; }

        /// <summary>0 to 1 as reported by the analyser, clamped on the way in.</summary>
        public double Confidence { get; set; }

        /// <summary>
        /// True when the value is uncertain enough that it must not be used until
        /// a person has looked at it. Anything below a high confidence, and
        /// anything commercial, is marked.
        /// </summary>
        public bool NeedsConfirmation { get; set; } = true;

        /// <summary>Set by a person in the review screen, never by the analyser.</summary>
        public bool Confirmed { get; set; }

        public bool HasValue => !string.IsNullOrWhiteSpace(Value);

        public static ExtractedValue Empty => new() { Confidence = 0, NeedsConfirmation = true };
    }

    /// <summary>
    /// A candidate position read out of a supplied contract. Presented for review
    /// and never saved without confirmation.
    /// </summary>
    public sealed class ExtractedPositionDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? LineTotal { get; set; }
        public string? Currency { get; set; }
        public decimal? VatRatePercent { get; set; }

        /// <summary>
        /// The billing frequency as one of the application's own cycles, and null
        /// when the document's frequency has no equivalent here — weekly, per
        /// milestone, on a condition.
        ///
        /// Null means "this does not map", never "one-off". Treating the two the
        /// same is how a monthly charge became a single charge: the analyser
        /// reports the frequency in the document's words ("monatlich"), which
        /// matches no member of a five-value enum, so the fallback silently won.
        /// </summary>
        public string? BillingCycle { get; set; }

        /// <summary>The frequency in the document's own words, always kept.</summary>
        public string? BillingCyclePhrase { get; set; }

        /// <summary>
        /// How firm the money is: Committed, Estimated, Variable, Optional,
        /// Conditional, Unknown. Shown beside the figure, because a rate nobody has
        /// committed to reads exactly like an agreed price without it.
        /// </summary>
        public string? Commitment { get; set; }

        /// <summary>
        /// Links back to the commercial term this was read from, so the full
        /// structure — phases, separate delivery and billing frequencies, caps —
        /// can be recovered when the position is saved.
        /// </summary>
        public string? TermKey { get; set; }

        /// <summary>
        /// Whether this can honestly become a priced contract line.
        ///
        /// False for a charge with no calculable amount — an hourly rate with no
        /// agreed hours, a usage charge with no volume. A contract position carries
        /// a quantity and a line total and has no way to say "not stated", so
        /// adopting one of these would put a number on the contract that nobody
        /// agreed. It stays visible, and stays out of the priced lines.
        /// </summary>
        public bool CanBecomePosition { get; set; } = true;

        /// <summary>Why it cannot, in words, when <see cref="CanBecomePosition"/> is false.</summary>
        public string? BlockedReason { get; set; }

        public string? SourceText { get; set; }
        public double Confidence { get; set; }

        /// <summary>
        /// Off by default. A position only becomes real when a person ticks it.
        /// </summary>
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// Everything analysis read out of one supplied contract document.
    ///
    /// Absent fields stay null. The analyser is instructed never to fill a gap
    /// with a plausible value, and the reader below drops anything it cannot
    /// parse rather than guessing — a missing price has to stay missing, because
    /// the alternative is a contract that quietly claims a number nobody agreed.
    /// </summary>
    public sealed class ContractExtractionDto
    {
        public ExtractedValue Title { get; set; } = ExtractedValue.Empty;
        public ExtractedValue ContractType { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Purpose { get; set; } = ExtractedValue.Empty;

        /// <summary>BCP-47 where the analyser can manage it, e.g. "de", "en".</summary>
        public ExtractedValue Language { get; set; } = ExtractedValue.Empty;

        // Parties
        public ExtractedValue ProviderName { get; set; } = ExtractedValue.Empty;
        public ExtractedValue ProviderAddress { get; set; } = ExtractedValue.Empty;
        public ExtractedValue ProviderRepresentative { get; set; } = ExtractedValue.Empty;
        public ExtractedValue CustomerName { get; set; } = ExtractedValue.Empty;
        public ExtractedValue CustomerAddress { get; set; } = ExtractedValue.Empty;
        public ExtractedValue CustomerRepresentative { get; set; } = ExtractedValue.Empty;

        // Dates and term
        public ExtractedValue EffectiveDate { get; set; } = ExtractedValue.Empty;
        public ExtractedValue StartDate { get; set; } = ExtractedValue.Empty;
        public ExtractedValue EndDate { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Duration { get; set; } = ExtractedValue.Empty;
        public ExtractedValue RenewalRules { get; set; } = ExtractedValue.Empty;
        public ExtractedValue TerminationNotice { get; set; } = ExtractedValue.Empty;

        // Money
        public ExtractedValue TotalPrice { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Currency { get; set; } = ExtractedValue.Empty;
        public ExtractedValue VatRate { get; set; } = ExtractedValue.Empty;
        public ExtractedValue VatTreatment { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Discounts { get; set; } = ExtractedValue.Empty;
        public ExtractedValue BillingCycle { get; set; } = ExtractedValue.Empty;
        public ExtractedValue PaymentSchedule { get; set; } = ExtractedValue.Empty;
        public ExtractedValue PaymentDueDates { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Deposit { get; set; } = ExtractedValue.Empty;
        public ExtractedValue RecurringCharges { get; set; } = ExtractedValue.Empty;

        // Obligations and terms
        public ExtractedValue CustomerResponsibilities { get; set; } = ExtractedValue.Empty;
        public ExtractedValue ProviderResponsibilities { get; set; } = ExtractedValue.Empty;
        public ExtractedValue AcceptanceCriteria { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Revisions { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Assumptions { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Exclusions { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Warranty { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Liability { get; set; } = ExtractedValue.Empty;
        public ExtractedValue Confidentiality { get; set; } = ExtractedValue.Empty;
        public ExtractedValue IntellectualProperty { get; set; } = ExtractedValue.Empty;
        public ExtractedValue SignatureParties { get; set; } = ExtractedValue.Empty;
        public ExtractedValue OtherTerms { get; set; } = ExtractedValue.Empty;

        public List<ExtractedPositionDto> Positions { get; set; } = new();

        /// <summary>
        /// Things a person has to look at before this contract goes anywhere:
        /// a missing price, two totals that disagree, party details that do not
        /// match our records.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// True when the document names no price at all. Recorded explicitly so
        /// the absence is a finding rather than an oversight — and so that no
        /// layer downstream is tempted to fill it in.
        /// </summary>
        public bool PriceMissing { get; set; }

        [JsonIgnore]
        public bool HasItemisedPositions => Positions.Count > 0;
    }
}

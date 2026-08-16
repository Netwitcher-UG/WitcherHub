using WitcherHub.Domain.Commercial;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    /// <summary>
    /// One thing the analyser recognised in the source, before anything has been
    /// decided about what to do with it.
    ///
    /// This is the stage that stops every sentence mentioning money from becoming
    /// a billable line. A payment condition, a scope limitation and a service
    /// description all mention commercial matters and none of them is a charge;
    /// classifying first is what keeps a contract total from being the sum of
    /// every number in the document.
    /// </summary>
    public sealed class DetectedConceptDto
    {
        public string Key { get; set; } = Guid.NewGuid().ToString("n");

        public CommercialConceptKind Kind { get; set; } = CommercialConceptKind.Unclassified;

        /// <summary>What the analyser understood this to be about, in its words.</summary>
        public string? Summary { get; set; }

        /// <summary>The passage it was read from, quoted.</summary>
        public string? SourceSnippet { get; set; }

        public int? SourceOffset { get; set; }

        public double Confidence { get; set; }

        /// <summary>Why it was read this way, for a person checking the reading.</summary>
        public string? Reasoning { get; set; }

        public bool IsAmbiguous { get; set; }
        public string? Ambiguity { get; set; }

        /// <summary>
        /// Other concepts this one belongs with. Commercial facts about one charge
        /// are routinely scattered across a document — a description in one
        /// clause, its price in another, its term in a third — and the relation
        /// between them is what makes them one term rather than three.
        /// </summary>
        public List<string> RelatedKeys { get; set; } = new();

        /// <summary>
        /// How the analyser reads the relation: whether several prices are phases
        /// of one charge, separate charges, alternatives, or optional extras.
        /// Left null when it cannot tell, which sends the question to a person
        /// instead of picking one silently.
        /// </summary>
        public string? RelationKind { get; set; }
    }

    /// <summary>
    /// A proposed commercial term, in the shape the analyser is asked to answer
    /// in. Deliberately close to the domain model but string-tolerant: a model
    /// that cannot express a value should say what it saw rather than round it
    /// to something the schema accepts.
    /// </summary>
    public sealed class ProposedTermDto
    {
        public string Key { get; set; } = Guid.NewGuid().ToString("n");

        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }

        /// <summary>
        /// The pricing basis in the analyser's words. Normalised on arrival, and
        /// kept as given when it does not normalise, so an arrangement nobody
        /// anticipated is recorded rather than approximated.
        /// </summary>
        public string? PricingModel { get; set; }

        public decimal? Quantity { get; set; }
        public string? QuantityUnit { get; set; }
        public decimal? UnitRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public string? Currency { get; set; }

        /// <summary>How often it is billed, as stated.</summary>
        public string? BillingRecurrence { get; set; }

        /// <summary>How often it is delivered, which need not be the same.</summary>
        public string? DeliveryRecurrence { get; set; }

        public string? PaymentSchedule { get; set; }

        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public int? DurationCount { get; set; }
        public string? DurationUnit { get; set; }

        public decimal? MinimumCommitment { get; set; }
        public decimal? Cap { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? DiscountAmount { get; set; }

        public decimal? TaxRatePercent { get; set; }
        public string? TaxTreatment { get; set; }

        public bool? IsMandatory { get; set; }
        public string? Conditions { get; set; }
        public string? Notes { get; set; }

        /// <summary>
        /// How firm the money is, as the analyser reads it. This is the judgement
        /// that decides whether an amount reaches the committed contract value,
        /// so it is asked for explicitly rather than inferred from whether a
        /// number is present.
        /// </summary>
        public string? Commitment { get; set; }

        public List<ProposedPhaseDto> Phases { get; set; } = new();

        public string? SourceSnippet { get; set; }
        public double Confidence { get; set; }
        public string? Reasoning { get; set; }
        public bool IsAmbiguous { get; set; }
        public string? Ambiguity { get; set; }

        /// <summary>Things the analyser could not settle and a person must.</summary>
        public List<string> OpenQuestions { get; set; } = new();
    }

    /// <summary>A proposed pricing period within a term.</summary>
    public sealed class ProposedPhaseDto
    {
        public string? Label { get; set; }
        public int Sequence { get; set; }

        public string? StartDate { get; set; }
        public string? EndDate { get; set; }

        /// <summary>A boundary stated in words rather than as a date.</summary>
        public string? StartCondition { get; set; }
        public string? EndCondition { get; set; }

        public int? DurationCount { get; set; }
        public string? DurationUnit { get; set; }

        public string? PricingModel { get; set; }
        public decimal? Rate { get; set; }
        public string? Currency { get; set; }
        public decimal? Quantity { get; set; }
        public string? QuantityUnit { get; set; }
        public string? BillingRecurrence { get; set; }

        public decimal? DiscountPercent { get; set; }
        public decimal? DiscountAmount { get; set; }

        public string? Conditions { get; set; }
        public string? SourceSnippet { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Everything one pass over a source document produced: what was recognised,
    /// what of it is a charge, what the document says about the parties, and what
    /// could not be settled.
    /// </summary>
    public sealed class SemanticExtractionDto
    {
        /// <summary>BCP-47 where determinable. Never assumed.</summary>
        public string? DetectedLanguage { get; set; }

        public string? DocumentType { get; set; }
        public string? DocumentTitle { get; set; }
        public string? Purpose { get; set; }

        /// <summary>Everything recognised, classified, charge or not.</summary>
        public List<DetectedConceptDto> Concepts { get; set; } = new();

        /// <summary>Only the concepts that are actually charges.</summary>
        public List<ProposedTermDto> Terms { get; set; } = new();

        /// <summary>
        /// What the document says about the parties. Read as evidence about the
        /// document, never as an instruction to change our records.
        /// </summary>
        public Dictionary<string, string?> DetectedParties { get; set; } = new();

        /// <summary>Contract-level dates and terms, as stated.</summary>
        public Dictionary<string, string?> DetectedContractTerms { get; set; } = new();

        /// <summary>Things a person must resolve.</summary>
        public List<string> OpenQuestions { get; set; } = new();

        /// <summary>Things worth knowing before this is relied on.</summary>
        public List<string> Warnings { get; set; } = new();
    }
}

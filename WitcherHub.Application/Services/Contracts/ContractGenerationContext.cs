using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// Everything the generator is allowed to know, and how much each part counts.
    ///
    /// The generator used to be handed the positions and a currency and nothing
    /// else, so the wording it produced could not name the parties, the project,
    /// the term or the payment arrangement — and a document assembled from a
    /// supplied text had that text pasted into it instead, which is how the
    /// pasted source ended up printed above the contract.
    ///
    /// The sources are separated here because they do not carry equal weight.
    /// <see cref="SourceText"/> is somebody's old agreement, email or notes: it
    /// tells the generator what kind of contract is wanted, and it is the last
    /// word on nothing. Everything above it in this type was entered or reviewed
    /// by a person in this system, and where the two disagree the person wins.
    /// That ordering is stated in <see cref="Precedence"/> and repeated to the
    /// model in the prompt, because a model given two conflicting addresses will
    /// otherwise pick whichever it saw last.
    /// </summary>
    public sealed class ContractGenerationContext
    {
        /// <summary>
        /// Us. From company settings, never from a supplied document — a
        /// customer's old contract may name a different provider entirely.
        /// </summary>
        public required PartyContext Provider { get; init; }

        /// <summary>
        /// The customer, from the customer record reached through the project.
        /// Authoritative unless somebody changes the customer record itself.
        /// </summary>
        public required PartyContext Customer { get; init; }

        public required ProjectContext Project { get; init; }

        public required ContractDetailsContext Contract { get; init; }

        /// <summary>
        /// The confirmed positions, structured. Flattening these to prose was
        /// what lost the billing cycle, the commitment and the phases.
        /// </summary>
        public IReadOnlyList<ManualPositionDto> Positions { get; init; } =
            Array.Empty<ManualPositionDto>();

        /// <summary>What the positions add up to, calculated here rather than by the model.</summary>
        public PositionTotalsDto? Totals { get; init; }

        /// <summary>
        /// Commercial terms a person reviewed and confirmed out of a supplied
        /// document. Distinct from <see cref="SourceText"/>: these were agreed.
        /// </summary>
        public IReadOnlyList<ConfirmedTerm> ConfirmedTerms { get; init; } =
            Array.Empty<ConfirmedTerm>();

        /// <summary>
        /// Optional. A pasted agreement, email, offer or set of notes.
        ///
        /// Context for the generator and nothing more. It is never copied into
        /// the contract, and it never overrules anything above.
        /// </summary>
        public string? SourceText { get; init; }

        /// <summary>Free-text guidance for the wording only.</summary>
        public string? AdditionalInstructions { get; init; }

        /// <summary>Language of the document to produce, e.g. "de".</summary>
        public string Language { get; init; } = "de";

        public bool HasSourceText => !string.IsNullOrWhiteSpace(SourceText);

        /// <summary>
        /// The ordering the generator must respect, most authoritative first.
        ///
        /// Written once, here, and handed to the model as part of the prompt so
        /// the rule the code follows and the rule the model is told are the same
        /// rule.
        /// </summary>
        public static IReadOnlyList<string> Precedence =>
        [
            "values a person entered or confirmed in this system",
            "company master data from company settings",
            "customer master data from the customer record",
            "reviewed project and contract details",
            "confirmed contract positions",
            "reviewed commercial terms",
            "pasted source text (lowest — context only)"
        ];

        /// <summary>One party as the contract should name it.</summary>
        public sealed record PartyContext(
            string? Name,
            string? Address,
            string? Representative = null,
            string? Email = null,
            string? TaxId = null);

        public sealed record ProjectContext(
            string? Title,
            string? Description = null,
            DateOnly? StartDate = null,
            DateOnly? EndDate = null);

        public sealed record ContractDetailsContext(
            string ContractNo,
            string Currency,
            DateOnly? StartDate,
            DateOnly? EndDate,
            decimal? AgreedTotalNet = null,
            decimal? VatRatePercent = null,
            string? PaymentTerms = null,
            string? Introduction = null);

        /// <summary>A commercial fact a person ticked, with where it came from.</summary>
        public sealed record ConfirmedTerm(string Label, string Value);
    }
}

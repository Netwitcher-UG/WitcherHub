namespace WitcherHub.Infrastructure.Services.Contracts
{
    public class ContractTemplateOptions
    {
        public const string SectionName = "ContractTemplates";

        public string FixedTermsDePath { get; set; } = "OpenAI/Contracts/fixed_terms_de.md";

        public string BaseDePath { get; set; } = "OpenAI/Contracts/Agenturvertrag.de.md";
        public string AgbDePath { get; set; } = "OpenAI/Contracts/AGB.de.md";
        public string ProviderBlock { get; set; } =
           "Netwitcher UG (haftungsbeschränkt)\n" +
           "Kochhannstraße 6\n" +
           "10249 Berlin\n" +
           "Deutschland\n";


        /// <summary>
        /// The provider's seat, for the jurisdiction clause.
        ///
        /// Not defaulted. A Gerichtsstand naming the wrong town is worse than a
        /// contract that has none, so the clause is simply left out while this
        /// is unset and the omission is reported.
        /// </summary>
        public string? ProviderSeat { get; set; }

        /// <summary>
        /// "Schriftform" or "Textform". A legal decision with different
        /// consequences, so it is configured rather than guessed.
        /// </summary>
        public string? FormRequirement { get; set; }

        /// <summary>
        /// Which document wins on a conflict, as the list the clause prints.
        /// </summary>
        public string? DocumentPrecedence { get; set; }

        /// <summary>
        /// The house payment term in days, until the contract itself carries one.
        ///
        /// The payment clause is one of the standing ones — it goes into every
        /// contract — and it cannot be rendered without a number. Nothing in the
        /// record supplies one yet, so without this setting every generated
        /// contract blocks on the same missing field forever.
        ///
        /// This is the owner stating their own standard term, which is a
        /// different thing from a number being invented to fill a gap: it is
        /// configured once, deliberately, and it is not defaulted here. Unset
        /// still means the clause is left out and the omission reported.
        ///
        /// Superseded per contract once the agreed term is a column on the
        /// contract itself.
        /// </summary>
        public int? PaymentDueDays { get; set; }

        /// <summary>
        /// The clause library version a lawyer has released, as the owner
        /// declares it — for example "1.0.0".
        ///
        /// Every module in the library ships as
        /// <see cref="Application.Services.Contracts.Clauses.ClauseReviewStatus.PendingLegalReview"/>,
        /// which is the honest state for wording nobody has signed off, and an
        /// unreleased module blocks approval. With no way to say "this has now
        /// been reviewed" that is not caution but a dead end: no generated
        /// contract could ever become a contract.
        ///
        /// So the release is a setting, and it names a version rather than being
        /// a boolean. Adding a clause or changing wording bumps
        /// <see cref="Application.Services.Contracts.Clauses.ContractClauseLibrary.LibraryVersion"/>,
        /// this no longer matches, and approval blocks again until the new
        /// wording has been through the same review — which is the behaviour a
        /// blanket "legal has approved the library" flag would silently lose.
        ///
        /// Unset means unreleased. Nothing auto-approves.
        /// </summary>
        public string? ClauseLibraryApprovedVersion { get; set; }
    }
}

namespace WitcherHub.Application.Services.Contracts.Delivery
{
    /// <summary>
    /// Why a contract may not be sent to a customer.
    ///
    /// Each one is a separate refusal because each has a different remedy, and
    /// "the contract cannot be sent" told the owner none of them.
    /// </summary>
    public enum DeliveryRefusal
    {
        None = 0,

        /// <summary>No approved version. A draft is not an offer.</summary>
        NotApproved,

        /// <summary>Approved once, then replaced. The old wording is not sendable.</summary>
        Superseded,

        /// <summary>Already signed. There is nothing left to ask for.</summary>
        AlreadySigned,

        /// <summary>The approved version carries no wording at all.</summary>
        NoContent,

        /// <summary>No usable customer e-mail, and e-mail was the chosen method.</summary>
        NoRecipient,

        /// <summary>Several addresses on file and none chosen.</summary>
        AmbiguousRecipient,

        /// <summary>The address given is not one of the customer's.</summary>
        UnknownRecipient,

        /// <summary>No public base URL configured, so any link built would be unreachable.</summary>
        NoPublicUrl,

        /// <summary>The e-mail did not go out. The request is not marked sent.</summary>
        DeliveryFailed
    }

    /// <summary>
    /// One customer e-mail address the contract could go to.
    /// </summary>
    /// <param name="Address">The address itself.</param>
    /// <param name="Label">Where it came from — a contact, a billing address — so
    /// the person choosing knows which is which.</param>
    /// <param name="IsPrimary">Whether the record marks it as the main one.</param>
    public sealed record DeliveryRecipient(string Address, string Label, bool IsPrimary);

    /// <summary>
    /// What a delivery attempt came to.
    /// </summary>
    public sealed record ContractDeliveryResult
    {
        public required bool Succeeded { get; init; }

        public DeliveryRefusal Refusal { get; init; } = DeliveryRefusal.None;

        /// <summary>What to show the user. Never a token, never a stack trace.</summary>
        public string? Message { get; init; }

        /// <summary>
        /// The complete signing URL, including the token.
        ///
        /// Returned exactly once, to the caller that created the request, so it
        /// can be copied or e-mailed. It is never stored, never logged and never
        /// rendered into a page — only the hash of its token is kept, so a leaked
        /// database cannot be used to sign anything.
        /// </summary>
        public string? SigningUrl { get; init; }

        public Guid? RequestId { get; init; }
        public int? SentVersion { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }

        /// <summary>The addresses to choose between, when that is the refusal.</summary>
        public IReadOnlyList<DeliveryRecipient> Choices { get; init; } = [];

        public static ContractDeliveryResult Refused(DeliveryRefusal refusal, string message) =>
            new() { Succeeded = false, Refusal = refusal, Message = message };

        public static ContractDeliveryResult NeedsChoice(IReadOnlyList<DeliveryRecipient> choices) =>
            new()
            {
                Succeeded = false,
                Refusal = DeliveryRefusal.AmbiguousRecipient,
                Choices = choices,
                Message = "Für diesen Kunden sind mehrere E-Mail-Adressen hinterlegt. " +
                          "Bitte wählen Sie die Adresse aus, an die der Vertrag gehen soll."
            };
    }

    /// <summary>
    /// The Terms the customer is asked to accept, with enough to prove later
    /// which wording that was.
    ///
    /// A URL on its own proves nothing. The page behind it can change the day
    /// after somebody signs, and then nobody can say what was accepted — which
    /// is the one question a dispute about terms of business turns on. The
    /// version and a hash of the wording in force are recorded with every
    /// request.
    /// </summary>
    public sealed class ContractTermsOptions
    {
        public const string SectionName = "ContractTerms";

        public string Url { get; set; } = "https://netwitcher.com/de/agb-fuer-agenturen";

        /// <summary>
        /// The edition in force, as the owner maintains it — "2026-01" or
        /// "v3". Changing the published Terms means changing this, and requests
        /// created afterwards record the new one.
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// A hash of the Terms wording as published under <see cref="Version"/>.
        ///
        /// Deliberately configured rather than fetched: downloading the page at
        /// send time would make issuing a contract depend on a public website
        /// being up, and would record whatever that page happened to say rather
        /// than what the owner released.
        /// </summary>
        public string Hash { get; set; } = "";

        /// <summary>How long a signing link stays usable.</summary>
        public int LinkValidDays { get; set; } = 14;
    }
}

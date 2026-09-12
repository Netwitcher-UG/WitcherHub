using System.Text.Json.Serialization;

namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>What a field is, so it can be set in the register that suits it.</summary>
    public enum TranslatableContentType
    {
        /// <summary>A heading or a short label. Sentence case, no full stop.</summary>
        Heading = 0,

        /// <summary>Running prose. Full sentences.</summary>
        Paragraph = 1,

        /// <summary>One entry of a list. Kept short and parallel with its siblings.</summary>
        ListItem = 2
    }

    /// <summary>Why a value must survive translation untouched.</summary>
    public enum ProtectedValueKind
    {
        LegalCompanyName = 0,
        PersonName = 1,
        Brand = 2,
        Product = 3,
        Url = 4,
        Email = 5,
        PhoneNumber = 6,
        PostalAddress = 7,
        Identifier = 8,
        Money = 9,
        Date = 10,
        Quantity = 11,
        Percentage = 12,
        Duration = 13
    }

    /// <summary>What became of one field.</summary>
    public enum TranslationStatus
    {
        /// <summary>It was in another language and is now German.</summary>
        Translated = 0,

        /// <summary>It was already German and was left as it was, or only tidied.</summary>
        AlreadyGerman = 1,

        /// <summary>It could not be translated safely. The source is unchanged and a flag says why.</summary>
        NeedsReview = 2
    }

    /// <summary>How serious a review flag is.</summary>
    public enum ReviewSeverity
    {
        Warning = 0,

        /// <summary>The contract may not be approved while this stands.</summary>
        Blocking = 1
    }

    /// <summary>
    /// One piece of contract text to be put into German.
    /// </summary>
    /// <param name="FieldId">
    /// Stable, and the only thing tying an answer back to the place it came
    /// from. The model is required to echo it; an answer that renames, drops or
    /// invents one is rejected rather than matched up by position.
    /// </param>
    /// <param name="Section">Where it sits in the contract, for the model's context only.</param>
    /// <param name="SourceText">The text as entered, in whatever language it was entered in.</param>
    /// <param name="ContentType">Heading, paragraph or list item.</param>
    public sealed record TranslatableField(
        string FieldId,
        string Section,
        string SourceText,
        TranslatableContentType ContentType);

    /// <summary>
    /// A value that must come back exactly as it went in.
    ///
    /// Before the call each occurrence in the source is replaced by
    /// <see cref="Token"/>. The model therefore never sees "Netwitcher UG
    /// (haftungsbeschränkt)" and cannot render it as "Netwitcher Ltd", translate
    /// "haftungsbeschränkt", or drop the legal form — it sees an opaque marker
    /// it is told to carry through untouched. Afterwards each marker is counted
    /// and swapped back, and a marker that moved, multiplied or vanished fails
    /// the whole field.
    /// </summary>
    public sealed record ProtectedValue(string Token, string Value, ProtectedValueKind Kind);

    /// <summary>
    /// The words this contract uses for the two sides and for the things it
    /// talks about, so a document does not drift between Kunde, Auftraggeber and
    /// Vertragspartner for the same role.
    /// </summary>
    public sealed record ContractTerminology
    {
        public string Provider { get; init; } = "Anbieter";
        public string Customer { get; init; } = "Kunde";
        public string Parties { get; init; } = "Parteien";
        public string Contract { get; init; } = "Vertrag";
        public string Service { get; init; } = "Leistung";
        public string ServiceScope { get; init; } = "Leistungsumfang";
        public string Deliverables { get; init; } = "Liefergegenstände";
        public string CustomerObligations { get; init; } = "Mitwirkungspflichten";
        public string Remuneration { get; init; } = "Vergütung";
        public string Acceptance { get; init; } = "Abnahme";
        public string Term { get; init; } = "Vertragslaufzeit";
        public string Termination { get; init; } = "Kündigung";
        public string UsageRights { get; init; } = "Nutzungsrechte";
        public string Confidentiality { get; init; } = "Vertraulichkeit";

        public static ContractTerminology Standard { get; } = new();
    }

    /// <summary>
    /// One normalization request: the fields to translate, the values that must
    /// not change, and the vocabulary to use.
    /// </summary>
    public sealed record ContractNormalizationRequest
    {
        public required IReadOnlyList<TranslatableField> Fields { get; init; }

        public IReadOnlyList<ProtectedValue> ProtectedValues { get; init; } = [];

        public ContractTerminology Terminology { get; init; } = ContractTerminology.Standard;

        /// <summary>"de". Present because a schema with one allowed value still
        /// has to say which one, and because a future second target is a
        /// configuration change rather than a rewrite.</summary>
        public string TargetLanguage { get; init; } = "de";
    }

    // ===================================================== what comes back

    public sealed class TranslatedFieldDto
    {
        [JsonPropertyName("fieldId")]
        public string FieldId { get; set; } = "";

        [JsonPropertyName("translatedText")]
        public string TranslatedText { get; set; } = "";

        /// <summary>ar | en | fr | tr | ku | de | other | unknown</summary>
        [JsonPropertyName("detectedSourceLanguage")]
        public string DetectedSourceLanguage { get; set; } = "unknown";

        /// <summary>translated | already_german | needs_review</summary>
        [JsonPropertyName("translationStatus")]
        public string TranslationStatus { get; set; } = "translated";

        public TranslationStatus Status => TranslationStatus?.Trim().ToLowerInvariant() switch
        {
            "already_german" => Language.TranslationStatus.AlreadyGerman,
            "needs_review" => Language.TranslationStatus.NeedsReview,
            _ => Language.TranslationStatus.Translated
        };
    }

    public sealed class TranslationReviewFlagDto
    {
        [JsonPropertyName("fieldId")]
        public string FieldId { get; set; } = "";

        [JsonPropertyName("sourceExcerpt")]
        public string SourceExcerpt { get; set; } = "";

        [JsonPropertyName("issue")]
        public string Issue { get; set; } = "";

        [JsonPropertyName("recommendedAction")]
        public string RecommendedAction { get; set; } = "";

        /// <summary>warning | blocking</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "warning";

        public ReviewSeverity Level =>
            string.Equals(Severity?.Trim(), "blocking", StringComparison.OrdinalIgnoreCase)
                ? ReviewSeverity.Blocking
                : ReviewSeverity.Warning;

        public override string ToString() =>
            $"{FieldId}: {Issue}" +
            (string.IsNullOrWhiteSpace(RecommendedAction) ? "" : $" — {RecommendedAction}");
    }

    /// <summary>The model's answer, in the shape the schema demands.</summary>
    public sealed class ContractNormalizationAnswer
    {
        [JsonPropertyName("targetLanguage")]
        public string TargetLanguage { get; set; } = "de";

        [JsonPropertyName("translatedFields")]
        public List<TranslatedFieldDto> TranslatedFields { get; set; } = [];

        [JsonPropertyName("reviewFlags")]
        public List<TranslationReviewFlagDto> ReviewFlags { get; set; } = [];

        [JsonPropertyName("canUseForContract")]
        public bool CanUseForContract { get; set; }
    }

    /// <summary>
    /// The normalization, after every deterministic check has run.
    ///
    /// <see cref="Text"/> is keyed by field id and is what the caller writes
    /// back. It is only ever complete: a run that could not produce a usable
    /// answer for every field fails outright rather than returning most of a
    /// contract, because a contract missing one scope paragraph reads as a
    /// finished contract.
    /// </summary>
    public sealed record ContractNormalizationResult
    {
        public required bool Succeeded { get; init; }

        /// <summary>Field id to German text. Empty when <see cref="Succeeded"/> is false.</summary>
        public IReadOnlyDictionary<string, string> Text { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Field id to the language it was written in.</summary>
        public IReadOnlyDictionary<string, string> DetectedLanguages { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Everything that needs a person to look at it.</summary>
        public IReadOnlyList<string> BlockingIssues { get; init; } = [];

        public IReadOnlyList<string> Warnings { get; init; } = [];

        /// <summary>Why the run failed, when it did. Never the model's raw output.</summary>
        public string? FailureReason { get; init; }

        /// <summary>What produced this, recorded with the version it belongs to.</summary>
        public required NormalizationProvenance Provenance { get; init; }

        /// <summary>
        /// True when the result may be used for a contract: it succeeded and
        /// nothing blocking came out of it.
        /// </summary>
        public bool CanUseForContract => Succeeded && BlockingIssues.Count == 0;

        public static ContractNormalizationResult Failed(
            string reason, NormalizationProvenance provenance) =>
            new()
            {
                Succeeded = false,
                FailureReason = reason,
                BlockingIssues = [reason],
                Provenance = provenance
            };
    }

    /// <summary>
    /// Which instructions, which schema and which model produced a translation.
    ///
    /// Recorded against the version rather than inferred later: two contracts
    /// normalized under different prompts are not comparable, and a year from
    /// now the only way to know which wording a given document came from is to
    /// have written it down at the time. None of it reaches the signed PDF.
    /// </summary>
    public sealed record NormalizationProvenance(
        string PromptVersion,
        string SchemaVersion,
        string Model,
        DateTimeOffset At,
        int FieldCount,
        int BatchCount);
}

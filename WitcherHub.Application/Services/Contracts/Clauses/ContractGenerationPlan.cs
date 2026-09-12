using System.Text.Json.Serialization;

namespace WitcherHub.Application.Services.Contracts.Clauses
{
    /// <summary>
    /// What the model is allowed to decide about a contract.
    ///
    /// Everything here is either a description of the work or a selection from a
    /// closed list. There is no money in it, no date, no party, no notice period
    /// — those are read from the record and computed in code, because a number a
    /// model produced is a number nobody agreed to.
    /// </summary>
    public sealed class ContractGenerationPlan
    {
        [JsonPropertyName("contractClassification")]
        public ContractClassificationDto Classification { get; set; } = new();

        [JsonPropertyName("serviceSections")]
        public List<ServiceSectionDto> ServiceSections { get; set; } = [];

        /// <summary>
        /// Chosen from the allowed list and nowhere else. Checked against the
        /// library on the way back in; an unknown id fails the whole plan rather
        /// than being quietly dropped, because a model inventing clause ids is a
        /// model that has stopped following the schema.
        /// </summary>
        [JsonPropertyName("selectedClauseModuleIds")]
        public List<string> SelectedClauseModuleIds { get; set; } = [];

        [JsonPropertyName("missingInformation")]
        public List<MissingInformationDto> MissingInformation { get; set; } = [];

        [JsonPropertyName("reviewFlags")]
        public List<ReviewFlagDto> ReviewFlags { get; set; } = [];

        [JsonPropertyName("canGenerateFinalContract")]
        public bool CanGenerateFinalContract { get; set; }

        /// <summary>
        /// The language the descriptive content came back in. Always "de" — the
        /// model is asked to state it so that a run which quietly answered in the
        /// input's language is a parse this application can see rather than a
        /// contract a customer cannot read.
        /// </summary>
        [JsonPropertyName("outputLanguage")]
        public string OutputLanguage { get; set; } = "de";

        /// <summary>
        /// What the model translated, and from what. Administrative: it is used
        /// to check the work and to show a reviewer which passages were not
        /// written by the person who entered them, and it never reaches the
        /// contract itself.
        /// </summary>
        [JsonPropertyName("translatedFields")]
        public List<TranslatedFieldDto> TranslatedFields { get; set; } = [];

        /// <summary>
        /// What the model deliberately left in its original language, and why.
        ///
        /// This is the half that stops the language check from being hostile: a
        /// customer whose company name is written in Arabic script must be able
        /// to receive a contract, and the name in it must be their name. Anything
        /// declared here is excluded before any script is counted.
        /// </summary>
        [JsonPropertyName("preservedTerms")]
        public List<PreservedTermDto> PreservedTerms { get; set; } = [];
    }

    public sealed class TranslatedFieldDto
    {
        [JsonPropertyName("field")]
        public string Field { get; set; } = "";

        /// <summary>ar | en | other | unknown</summary>
        [JsonPropertyName("sourceLanguage")]
        public string SourceLanguage { get; set; } = "unknown";

        [JsonPropertyName("translatedText")]
        public string TranslatedText { get; set; } = "";
    }

    public sealed class PreservedTermDto
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "";

        /// <summary>company_name | person_name | brand | product | url | identifier</summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";
    }

    public sealed class ContractClassificationDto
    {
        /// <summary>service | work | mixed</summary>
        [JsonPropertyName("overallType")]
        public string OverallType { get; set; } = "service";

        /// <summary>one_time | recurring | mixed</summary>
        [JsonPropertyName("recurrence")]
        public string Recurrence { get; set; } = "one_time";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        public ServiceNature Nature => OverallType switch
        {
            "work" => ServiceNature.Work,
            "mixed" => ServiceNature.Mixed,
            _ => ServiceNature.Service
        };

        public ServiceRecurrence RecurrenceKind => Recurrence switch
        {
            "recurring" => ServiceRecurrence.Recurring,
            "mixed" => ServiceRecurrence.Mixed,
            _ => ServiceRecurrence.OneTime
        };
    }

    /// <summary>
    /// One position, described. This is the part of a contract that genuinely
    /// differs per project and is the only prose the model writes.
    /// </summary>
    public sealed class ServiceSectionDto
    {
        [JsonPropertyName("serviceItemId")]
        public string ServiceItemId { get; set; } = "";

        [JsonPropertyName("serviceType")]
        public string ServiceType { get; set; } = "service";

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "";

        [JsonPropertyName("deliverables")]
        public List<string> Deliverables { get; set; } = [];

        [JsonPropertyName("outOfScope")]
        public List<string> OutOfScope { get; set; } = [];

        [JsonPropertyName("customerObligations")]
        public List<string> CustomerObligations { get; set; } = [];

        /// <summary>
        /// Empty for a pure service with nothing objectively acceptable. That is
        /// not a gap: acceptance belongs to Werkvertragsrecht, and listing
        /// criteria for advisory work invents an obligation.
        /// </summary>
        [JsonPropertyName("acceptanceCriteria")]
        public List<string> AcceptanceCriteria { get; set; } = [];

        [JsonPropertyName("assumptions")]
        public List<string> Assumptions { get; set; } = [];

        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = [];

        [JsonPropertyName("revisionRules")]
        public List<string> RevisionRules { get; set; } = [];

        [JsonPropertyName("riskNotes")]
        public List<string> RiskNotes { get; set; } = [];

        public ServiceNature Nature => ServiceType switch
        {
            "work" => ServiceNature.Work,
            "mixed" => ServiceNature.Mixed,
            _ => ServiceNature.Service
        };
    }

    public sealed class MissingInformationDto
    {
        [JsonPropertyName("field")]
        public string Field { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        /// <summary>blocking | warning</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "warning";

        public bool IsBlocking =>
            string.Equals(Severity, "blocking", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ReviewFlagDto
    {
        [JsonPropertyName("section")]
        public string Section { get; set; } = "";

        [JsonPropertyName("issue")]
        public string Issue { get; set; } = "";

        [JsonPropertyName("recommendedAction")]
        public string RecommendedAction { get; set; } = "";
    }
}

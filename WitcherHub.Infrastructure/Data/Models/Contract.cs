using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Contract : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        [MaxLength(50)]
        public string ContractNo { get; set; } = default!;

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public bool ApplyVat { get; set; } = true;
        public string? Terms { get; set; }
        public bool? FromQuote { get; set; } = false;

        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }
        public JsonDocument? TermsStructured { get; set; }
        public DateTimeOffset? SignedAt { get; set; }

        public InvoiceSendMode InvoiceSendMode { get; set; } = InvoiceSendMode.Automatic;

        // =====================================================================
        // What the contract is built from
        //
        // Recorded rather than inferred from Items.Count. A contract whose
        // wording is a document the customer supplied has no positions and never
        // will; counting rows cannot tell that apart from an unfinished one, and
        // treating the two the same is what blocked supplied-text contracts.
        // Existing contracts default to Positions, which is what they are.
        // =====================================================================

        public ContractSourceMode SourceMode { get; set; } = ContractSourceMode.Positions;

        // ---- where the contract stands, recorded rather than inferred --------
        //
        // Reading these off a position count and a version number is what made
        // the screen unable to say which version was the original, which was the
        // analysis, and which was ready to sign. Each is stored.

        public ContractSourceState SourceState { get; set; } = ContractSourceState.None;
        public ContractReviewState ReviewState { get; set; } = ContractReviewState.NotRequired;
        public ContractPreparationState PreparationState { get; set; } = ContractPreparationState.NoPreparedDraft;

        /// <summary>
        /// The version currently approved. A pointer rather than a flag search,
        /// so "the active contract" is one lookup and cannot come back with two
        /// answers.
        /// </summary>
        public Guid? ApprovedDraftId { get; set; }

        /// <summary>
        /// The last preparation request accepted. A second request carrying the
        /// same key returns the draft the first one made instead of making
        /// another, so a double click cannot produce two versions.
        /// </summary>
        [MaxLength(64)]
        public string? LastPreparationKey { get; set; }

        public Guid? LastPreparedDraftId { get; set; }

        /// <summary>
        /// The agreed total when the contract names one lump sum instead of
        /// itemised services. Null means no contract-level total was agreed —
        /// which is not the same as a total of zero, and is why this is nullable
        /// rather than defaulted.
        /// </summary>
        [Column(TypeName = "numeric(14,2)")]
        public decimal? AgreedTotalNet { get; set; }

        [Column(TypeName = "numeric(6,3)")]
        public decimal? AgreedTotalVatRatePercent { get; set; }

        /// <summary>
        /// True once a person has confirmed that this contract genuinely names no
        /// price. Without it, a missing price is an open question rather than a
        /// decision, and the contract shows a warning instead of quietly reading
        /// as free of charge.
        /// </summary>
        public bool PriceDeliberatelyUnspecified { get; set; }

        [MaxLength(2000)]
        public string? PaymentTermsText { get; set; }

        /// <summary>
        /// Who the parties were when the contract was prepared, taken from the
        /// company configuration and the customer record at that moment. Kept so
        /// that a later change to either does not silently rewrite what was
        /// signed.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? PartySnapshot { get; set; }

        /// <summary>SHA-256 of the exact document that was signed.</summary>
        [MaxLength(64)]
        public string? SignedDocumentHash { get; set; }

        /// <summary>The version number of the draft that was signed.</summary>
        public int? SignedDraftVersion { get; set; }

        /// <summary>
        /// What the contract adds up to, as the deterministic engine calculated
        /// it: committed amounts separated from estimated, variable and optional
        /// ones, with the reason for anything that could not be resolved.
        ///
        /// Stored rather than recomputed on every read so that the figure shown
        /// to a customer is the figure that was calculated when the terms were
        /// last agreed, not one produced by a later change to the engine.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? FinancialSummary { get; set; }

        // =========================
        // Serienrechnung / Recurring
        // =========================
        public bool RecurringEnabled { get; set; } = false;

        public bool RecurringIsActive { get; set; } = false;

        public DateOnly? RecurringStartDate { get; set; }

        public DateOnly? RecurringEndDate { get; set; }

        public DateOnly? NextRecurringInvoiceDate { get; set; }

        public DateTimeOffset? LastRecurringInvoiceRunAt { get; set; }

        public ICollection<ContractItem> Items { get; set; } = new List<ContractItem>();
        public ICollection<ContractSignature> Signatures { get; set; } = new List<ContractSignature>();
        public ICollection<ContractDraft> Drafts { get; set; } = new List<ContractDraft>();
    }

    public class ContractItem : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        [MaxLength(80)]
        public Guid? ServiceId { get; set; }
        public ServiceCatalogItem? Service { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        [Column(TypeName = "jsonb")]
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        [Column(TypeName = "numeric(12,2)")]
        public decimal? AgreedPrice { get; set; }
        [MaxLength(500)]
        public string Description { get; set; } = null!;


        public ServiceUnitType UnitType { get; set; } = ServiceUnitType.Custom;

        [MaxLength(30)]
        public string UnitName { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; } = 0m;

        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;

        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }

        public JsonDocument? PriceBreakdown { get; set; }
        public int Position { get; set; } = 1;

        // =====================================================================
        // Manual positions
        //
        // A position may be entered by hand with no ServiceCatalogItem behind it.
        // ServiceId stays null in that case — deliberately not a placeholder
        // catalog record — and Source records how the position was created.
        // =====================================================================

        public ContractItemSource Source { get; set; } = ContractItemSource.Catalog;

        public bool IsManual => Source == ContractItemSource.Manual;

        [MaxLength(60)]
        public string? ServiceTypeLabel { get; set; }

        [MaxLength(40)]
        public string? PricingModelName { get; set; }

        [MaxLength(10)]
        public string? Currency { get; set; }

        [Column(TypeName = "numeric(6,3)")]
        public decimal? VatRatePercent { get; set; }

        /// <summary>
        /// Number of billing periods agreed, for a recurring billing cycle.
        /// </summary>
        public int? DurationPeriods { get; set; }

        public ActivationMethod ActivationMethod { get; set; } = ActivationMethod.NotApplicable;

        public DateOnly? StartDate { get; set; }
        public DateOnly? DeliveryDate { get; set; }

        /// <summary>
        /// True when the position is deliberately supplied at no charge, which is
        /// the only case where a price is not required.
        /// </summary>
        public bool IsFree { get; set; }

        /// <summary>
        /// The agreed terms as they stood when the position was accepted: title,
        /// description, scope, deliverables, price, quantity, tax, discount, cycle,
        /// duration and dates.
        ///
        /// Written once and not rewritten by later catalog edits, so a signed
        /// contract keeps the wording and figures that were actually agreed.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? Snapshot { get; set; }

        public DateTimeOffset? SnapshotTakenAt { get; set; }

        /// <summary>
        /// The supplied contract version this position was read out of, when it
        /// came from one. Keeps an extracted position attached to the document
        /// that justifies it, so it can be traced back and re-checked.
        /// </summary>
        public Guid? SourceDraftId { get; set; }

        // =====================================================================
        // The generic commercial term
        //
        // The flat columns above can express a quantity times a rate on one of
        // five billing cycles. That is one shape of agreement. Pricing that
        // changes partway through, a rate with no committed quantity, a charge
        // that recurs fortnightly, an arrangement indexed to something external —
        // none of them fits, and forcing them in states an agreement nobody made.
        //
        // The full CommercialTerm lives here as written data. The flat columns
        // are kept in step for the existing workflow and for anything that reads
        // a position without needing the whole structure.
        // =====================================================================

        [Column(TypeName = "jsonb")]
        public JsonDocument? CommercialTerm { get; set; }

        /// <summary>
        /// True once a person has edited or accepted this term, which stops a
        /// later analysis from replacing it without being asked.
        /// </summary>
        public bool IsHumanReviewed { get; set; }
    }

    /// <summary>
    /// One generated version of the contract text.
    ///
    /// The structured positions remain the source of truth; this stores the
    /// generated wording alongside the provenance needed to reproduce or audit it.
    /// Versions accumulate rather than overwrite so drafts can be compared, and an
    /// approved version is never replaced without an explicit decision.
    /// </summary>
    public class ContractDraft : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        public int Version { get; set; } = 1;

        public string DocumentMarkdown { get; set; } = "";

        /// <summary>
        /// The positions exactly as they were when this draft was generated.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? PositionsSnapshot { get; set; }

        [MaxLength(40)]
        public string? PromptVersion { get; set; }

        [MaxLength(40)]
        public string? TemplateVersion { get; set; }

        [MaxLength(80)]
        public string? Model { get; set; }

        [MaxLength(40)]
        public string? GeneratedBy { get; set; }

        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

        public bool IsApproved { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public Guid? ApprovedById { get; set; }

        /// <summary>
        /// SHA-256 of the approved document, so the text that was signed can be
        /// shown to have been the text that was approved.
        /// </summary>
        [MaxLength(64)]
        public string? DocumentHash { get; set; }

        // =====================================================================
        // Supplied source documents
        //
        // A version can also be a contract the customer supplied. That is a
        // different kind of thing from wording the system generated: it is the
        // source document, it is never rewritten, and everything generated from
        // it points back at it.
        // =====================================================================

        public ContractDraftKind Kind { get; set; } = ContractDraftKind.Generated;

        /// <summary>
        /// Where this version stands. Separate from <see cref="Kind"/>, which is
        /// what kind of thing it is: an approved supplied original and an
        /// approved prepared draft are both approved and are not the same thing.
        /// </summary>
        public ContractDraftStatus Status { get; set; } = ContractDraftStatus.Draft;

        /// <summary>
        /// Set when a later version was approved in this one's place. The version
        /// stays in the history — a superseded version is still the text somebody
        /// may have signed.
        /// </summary>
        public DateTimeOffset? SupersededAt { get; set; }

        /// <summary>
        /// True for a version that must never be edited: the document exactly as
        /// it was supplied. Edits produce a new version instead.
        /// </summary>
        public bool IsImmutableSource { get; set; }

        [MaxLength(20)]
        public string? SourceLanguage { get; set; }

        /// <summary>
        /// The supplied version this one was prepared from, for a generated
        /// version that merged party details into a supplied document.
        /// </summary>
        public Guid? SourceDraftId { get; set; }

        /// <summary>
        /// What analysis read out of a supplied document: values, where each came
        /// from, and how confident it was. Stored as given by the analyser and
        /// only promoted onto the contract once a person confirms it.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public JsonDocument? ExtractedTerms { get; set; }

        public ContractExtractionStatus ExtractionStatus { get; set; } = ContractExtractionStatus.NotAnalysed;

        public DateTimeOffset? ExtractedAt { get; set; }

        public DateTimeOffset? ExtractionConfirmedAt { get; set; }
    }

    public class ContractSignature : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        [MaxLength(200)]
        public string SignerName { get; set; } = default!;

        [MaxLength(320)]
        public string? SignerEmail { get; set; }

        public DateTimeOffset? SignedAt { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? SignatureData { get; set; }
    }
}

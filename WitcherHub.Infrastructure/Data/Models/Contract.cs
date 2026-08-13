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

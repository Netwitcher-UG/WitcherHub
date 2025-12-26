using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

using WitcherHub.Domain.Commen;
namespace WitcherHub.Infrastructure.Data.Models
{

    public class Invoice : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        public Guid? ContractId { get; set; }
        public Contract? Contract { get; set; }

        [MaxLength(50)]
        public string InvoiceNo { get; set; } = default!;

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public DateOnly? IssueDate { get; set; }
        public DateOnly? DueDate { get; set; }

        public DiscountType? InvoiceDiscountType { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? InvoiceDiscountValue { get; set; }

        public Guid? TaxRateId { get; set; }
        public TaxRate? TaxRate { get; set; }

        public string? Notes { get; set; }

        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }

        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }

        public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        public InvoiceTotal? Totals { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<MilestoneInvoice> MilestoneLinks { get; set; } = new List<MilestoneInvoice>();
    }

    public class InvoiceItem : BaseEntity
    {
        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [MaxLength(80)]
        public Guid? ServiceId { get; set; }
        public ServiceCatalogItem? Service { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        [Column(TypeName = "numeric(12,2)")]
        public decimal Quantity { get; set; } = 1;

        [Column(TypeName = "numeric(12,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        [Column(TypeName = "jsonb")]
        public JsonDocument? PriceBreakdown { get; set; }

        public Guid? TaxRateId { get; set; }
        public TaxRate? TaxRate { get; set; }

        public DiscountType? DiscountType { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? DiscountValue { get; set; }

        public int Position { get; set; } = 1;
    }

    public class InvoiceTotal
    {
        [Key]
        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [Column(TypeName = "numeric(12,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal DiscountTotal { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal TaxTotal { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal Total { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal PaidTotal { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal BalanceDue { get; set; }

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class Payment : BaseEntity
    {
        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [Column(TypeName = "numeric(12,2)")]
        public decimal Amount { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public PaymentMethod Method { get; set; } = PaymentMethod.Bank;
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        [MaxLength(200)]
        public string? ProviderRef { get; set; }

        public DateTimeOffset? PaidAt { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument? Meta { get; set; }
    }
}

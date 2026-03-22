using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Quote : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = default!;

        [MaxLength(50)]
        public string QuoteNo { get; set; } = default!;

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        [MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public string? Notes { get; set; }

        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool ApplyVat { get; set; } = false;
        public Guid? CreatedById { get; set; }
        public AppUser? CreatedBy { get; set; }

        public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
    }

    public class QuoteItem : BaseEntity
    {
        public Guid QuoteId { get; set; }
        public Quote Quote { get; set; } = default!;

        public Guid? ServiceId { get; set; }
        public ServiceCatalogItem? Service { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = default!;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public ServiceUnitType UnitType { get; set; } = ServiceUnitType.Custom;

        [MaxLength(30)]
        public string UnitName { get; set; } = string.Empty;

        [Column(TypeName = "numeric(12,2)")]
        public decimal Quantity { get; set; } = 1m;

        [Column(TypeName = "numeric(12,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        [Column(TypeName = "jsonb")]
        public JsonDocument? PriceBreakdown { get; set; }
        [MaxLength(500)]
        public string Description { get; set; } = null!;
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;
        public DiscountType? DiscountType { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? DiscountValue { get; set; }

        public int Position { get; set; } = 1;
    }
}


using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using WitcherHub.Domain.Commen;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class ServiceCatalogItem : BaseEntity
    {
        

        [MaxLength(250)]
        public string Name { get; set; } = default!;

        public ServiceType ServiceType { get; set; } = ServiceType.Other;
        public PricingModel PricingModel { get; set; } = PricingModel.Fixed;

        [Column(TypeName = "numeric(12,2)")]
        public decimal BasePrice { get; set; }

        [MaxLength(10)]
        public string DefaultCurrency { get; set; } = "EUR";

        public bool IsActive { get; set; } = true;
        [MaxLength(500)]
        public string Discription { get; set; } =null!;

        [Column(TypeName = "jsonb")]
        public JsonDocument? ConfigSchema { get; set; }

        public new DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public ICollection<PricingRule> PricingRules { get; set; } = new List<PricingRule>();

    }


    public class PricingRule : BaseEntity
    {
        [MaxLength(80)]
        public Guid ServiceId { get; set; } = default!;
        public ServiceCatalogItem Service { get; set; } = default!;

        [MaxLength(200)]
        public string Name { get; set; } = default!;

        public int Priority { get; set; } = 100;

        [MaxLength(2000)]
        public string ConditionExpr { get; set; } = "true";

        public RuleAction Action { get; set; }

        [MaxLength(2000)]
        public string ValueExpr { get; set; } = "0";

        [MaxLength(200)]
        public string? Label { get; set; }

        [MaxLength(30)]
        public string Scope { get; set; } = "LINE_ITEM"; // LINE_ITEM / INVOICE

        public bool IsActive { get; set; } = true;

        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }
}

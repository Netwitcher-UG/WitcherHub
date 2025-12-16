using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class TaxRate : BaseEntity
    {
        [MaxLength(200)]
        public string Name { get; set; } = default!;

        [Column(TypeName = "numeric(6,3)")]
        public decimal RatePercent { get; set; }

        [MaxLength(50)]
        public string? Country { get; set; }

        public bool IsActive { get; set; } = true;

        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }

    public class DiscountCode : BaseEntity
    {
        [MaxLength(60)]
        public string Code { get; set; } = default!;

        [MaxLength(200)]
        public string? Name { get; set; }

        public DiscountType DiscountType { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal Value { get; set; }

        public bool IsActive { get; set; } = true;

        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }

        public int? MaxUses { get; set; }
    }
}

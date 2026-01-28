using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Quotes
{
    public class QuoteViews
    {
        public class QuoteListItemView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string QuoteNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? IssuedAt { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }

            // اختياري: Totals إذا بدك تحسبها لاحقاً
            public decimal ItemsTotal { get; set; }
        }

        public class QuoteDetailsView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string QuoteNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public string? Notes { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset? IssuedAt { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }

            public List<QuoteItemItemView> Items { get; set; } = new();
        }

        public class QuoteItemItemView
        {
            public Guid Id { get; set; }
            public Guid? ServiceId { get; set; }
            public string? ServiceName { get; set; }

            public string Title { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }

            public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");
            public JsonDocument? PriceBreakdown { get; set; }

            public Guid? TaxRateId { get; set; }
            public string? TaxName { get; set; }

            public DiscountType? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }

            public int Position { get; set; }

            public decimal LineTotal { get; set; } // Quantity * UnitPrice - discount (بدون tax حالياً)
        }
    }
}

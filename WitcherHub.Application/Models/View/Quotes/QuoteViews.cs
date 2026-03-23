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

            public bool ApplyVat { get; set; }
            public QuoteAfterSignAction AfterCustomerSignAction { get; set; } = QuoteAfterSignAction.Contract;
            public InvoiceSendMode InvoiceSendMode { get; set; } = InvoiceSendMode.Automatic;
         
            public bool RecurringEnabled { get; set; }
            public bool RecurringIsActive { get; set; }
            public DateOnly? RecurringStartDate { get; set; }
            public DateOnly? RecurringEndDate { get; set; }
            public DateOnly? NextRecurringInvoiceDate { get; set; }
            public DateTimeOffset? LastRecurringInvoiceRunAt { get; set; }

            public DateTimeOffset? SignedAt { get; set; }
            public List<QuoteSignatureView> Signatures { get; set; } = new();
            public List<QuoteItemItemView> Items { get; set; } = new();
        }

        public class QuoteItemItemView
        {
            public Guid Id { get; set; }
            public Guid? ServiceId { get; set; }
            public string? ServiceName { get; set; }

            public string Title { get; set; } = "";
            public string Description { get; set; } = string.Empty;
            public string UnitName { get; set; } = string.Empty;

            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }

            public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");
            public JsonDocument? PriceBreakdown { get; set; }

            public BillingCycle BillingCycle { get; set; }

            public DiscountType? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }

            public int Position { get; set; }
            public decimal LineTotal { get; set; }
        }
    }
}

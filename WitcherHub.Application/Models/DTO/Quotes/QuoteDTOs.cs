using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Quotes
{
    public class QuoteDTOs
    {
        public QuoteDto Quote { get; set; } = new();
        public List<QuoteItemDto> Items { get; set; } = new();
    }

    public class QuoteDto
    {
        public Guid ProjectId { get; set; }

        public string Currency { get; set; } = "EUR";
        public string? Notes { get; set; }

        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool ApplyVat { get; set; } = false;
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    }

    public class QuoteItemDto
    {
        public Guid? ServiceId { get; set; }
        public string Title { get; set; } = "";

        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }

        // jsonb
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");
        public JsonDocument? PriceBreakdown { get; set; }

      
        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }
        public List<Guid> PricingRuleIds { get; set; } = new();
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;
        public int Position { get; set; } = 1;
    }

    public sealed class QuoteIdRequest
    {
        public Guid QuoteId { get; set; }
    }

    public class UpdateQuoteDto
    {
        public QuoteDto Quote { get; set; } = new();
        public List<QuoteItemDto>? Items { get; set; } = new();
    }

    // عمليات على Item بشكل منفصل (مثل Customer addresses)
    public class CreateQuoteItemDto
    {
        public Guid QuoteId { get; set; }
        public QuoteItemDto Item { get; set; } = new();
    }

    public class UpdateQuoteItemDto
    {
        public Guid QuoteId { get; set; }
        public Guid ItemId { get; set; }
        public QuoteItemDto Item { get; set; } = new();
    }

    public class DeleteQuoteItemDto
    {
        public Guid QuoteId { get; set; }
        public Guid ItemId { get; set; }
    }

    public class ReorderQuoteItemsDto
    {
        public Guid QuoteId { get; set; }
        public List<Guid> OrderedItemIds { get; set; } = new();
    }
}

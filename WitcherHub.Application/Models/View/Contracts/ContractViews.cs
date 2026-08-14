using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Contracts
{
    public class ContractViews
    {
        public class ContractListItemView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string ContractNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            public decimal ItemsTotal { get; set; }
        }

        public class ContractDetailsView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }

            public string ContractNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            // Who the contract is with. The builder has to show the parties, and
            // without these it could only show a contract number.
            public Guid CustomerId { get; set; }
            public string CustomerName { get; set; } = "";
            public string ProjectTitle { get; set; } = "";

            public string? Terms { get; set; }
            public JsonDocument? TermsStructured { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? StartDate { get; set; }
            public DateOnly? EndDate { get; set; }

            public DateTimeOffset? SignedAt { get; set; }
            public InvoiceSendMode InvoiceSendMode { get; set; } = InvoiceSendMode.Automatic;
            public List<ContractItemItemView> Items { get; set; } = new();
            public List<ContractSignatureView> Signatures { get; set; } = new();
        }

        public class ContractItemItemView
        {
            public Guid Id { get; set; }
            public Guid? ServiceId { get; set; }
            public string? ServiceName { get; set; }

            public decimal Quantity { get; set; } = 1m;
            public decimal UnitPrice { get; set; } = 0m;
            public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;

            public DiscountType? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }

            public JsonDocument? PriceBreakdown { get; set; }
            public string Title { get; set; } = "";
            public string Description { get; set; } = string.Empty;
            public string UnitName { get; set; } = string.Empty;

            public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

            public decimal? AgreedPrice { get; set; }
            public int Position { get; set; }
        }

        public class ContractSignatureView
        {
            public Guid Id { get; set; }
            public string SignerName { get; set; } = "";
            public string? SignerEmail { get; set; }
            public DateTimeOffset? SignedAt { get; set; }
            public JsonDocument? SignatureData { get; set; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Invoices
{
    public class InvoiceViews
    {
        public class InvoiceListItemView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }
            public Guid? ContractId { get; set; }

            public string InvoiceNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? IssueDate { get; set; }
            public DateOnly? DueDate { get; set; }

            public decimal ItemsTotal { get; set; }
            public decimal Total { get; set; }
            public decimal BalanceDue { get; set; }

            // Lexware
            public string? LexwareVoucherStatus { get; set; }
            public DateTimeOffset? LexwareSyncedAt { get; set; }
        }

        public class InvoiceDetailsView
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }
            public Guid? ContractId { get; set; }

            public string InvoiceNo { get; set; } = "";
            public DocumentStatus Status { get; set; }
            public string Currency { get; set; } = "EUR";

            public string? Notes { get; set; }

            public DateTimeOffset CreatedAt { get; set; }
            public DateOnly? IssueDate { get; set; }
            public DateOnly? DueDate { get; set; }

            public DateTimeOffset? IssuedAt { get; set; }
            public DateTimeOffset? PaidAt { get; set; }

            public Guid? TaxRateId { get; set; }
            public string? TaxName { get; set; }

            public DiscountType? InvoiceDiscountType { get; set; }
            public decimal? InvoiceDiscountValue { get; set; }

            public InvoiceTotalsView? Totals { get; set; }
            public List<InvoiceItemItemView> Items { get; set; } = new();

            // Lexware
            public string? LexwareInvoiceId { get; set; }
            public string? LexwareVoucherNumber { get; set; }
            public string? LexwareVoucherStatus { get; set; }
            public string? LexwareResourceUri { get; set; }
            public int? LexwareVersion { get; set; }
            public DateTimeOffset? LexwareSyncedAt { get; set; }
            public string? LexwarePdfPath { get; set; }
            public JsonDocument? LexwareSnapshot { get; set; }
        }

        public class InvoiceTotalsView
        {
            public decimal Subtotal { get; set; }
            public decimal DiscountTotal { get; set; }
            public decimal TaxTotal { get; set; }
            public decimal Total { get; set; }
            public decimal PaidTotal { get; set; }
            public decimal BalanceDue { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }

        public class InvoiceItemItemView
        {
            public Guid Id { get; set; }
            public Guid? ServiceId { get; set; }
            public string? ServiceName { get; set; }

            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string UnitName { get; set; } = "";

            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }

            public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");
            public JsonDocument? PriceBreakdown { get; set; }

            public Guid? TaxRateId { get; set; }
            public string? TaxName { get; set; }

            public DiscountType? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }

            public int Position { get; set; }
            public decimal LineTotal { get; set; }
        }
    }
}

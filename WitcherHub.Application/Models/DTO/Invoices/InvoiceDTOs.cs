using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Invoices
{
    public class InvoiceDTOs
    {
        public InvoiceDto Invoice { get; set; } = new();
        public List<InvoiceItemDto> Items { get; set; } = new();
        public string? LexwareInvoiceId { get; set; }
        public string? LexwareVoucherNumber { get; set; }
        public string? LexwareVoucherStatus { get; set; }
        public string? LexwarePdfPath { get; set; }
    }

    public class InvoiceDto
    {
        public Guid ProjectId { get; set; }
        public Guid? ContractId { get; set; }

        [System.ComponentModel.DataAnnotations.MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public string? Notes { get; set; }

        // Invoice header dates (preferred for UI)
        public DateOnly? IssueDate { get; set; }
        public DateOnly? DueDate { get; set; }

        // Optional timestamps
        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        // Header-level discount/tax (optional)
        public DiscountType? InvoiceDiscountType { get; set; }
        public decimal? InvoiceDiscountValue { get; set; }

        public Guid? TaxRateId { get; set; }
    }

    public class InvoiceItemDto
    {
        public Guid? ServiceId { get; set; }
        public string Title { get; set; } = "";

        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }

        // jsonb
        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");
        public JsonDocument? PriceBreakdown { get; set; }

        public Guid? TaxRateId { get; set; }
        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }

        public int Position { get; set; } = 1;
    }

    public class UpdateInvoiceDto
    {
        public InvoiceDto Invoice { get; set; } = new();
        public List<InvoiceItemDto>? Items { get; set; } = new();
    }

    // Item ops
    public class CreateInvoiceItemDto
    {
        public Guid InvoiceId { get; set; }
        public InvoiceItemDto Item { get; set; } = new();
    }

    public class UpdateInvoiceItemDto
    {
        public Guid InvoiceId { get; set; }
        public Guid ItemId { get; set; }
        public InvoiceItemDto Item { get; set; } = new();
    }

    public class DeleteInvoiceItemDto
    {
        public Guid InvoiceId { get; set; }
        public Guid ItemId { get; set; }
    }

    public class ReorderInvoiceItemsDto
    {
        public Guid InvoiceId { get; set; }
        public List<Guid> OrderedItemIds { get; set; } = new();
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public class LexwareActionResult
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("resourceUri")] public string ResourceUri { get; set; } = "";
        [JsonPropertyName("version")] public int Version { get; set; }
    }

    public class LexwareInvoiceCreateRequest
    {
        [JsonPropertyName("archived")] public bool Archived { get; set; } = false;

        // ✅ FIX: Lexware expects 3 fractional digits
        [JsonPropertyName("voucherDate")]
        [JsonConverter(typeof(LexwareDateTimeOffsetConverter))]
        public DateTimeOffset VoucherDate { get; set; }

        [JsonPropertyName("address")] public LexwareInvoiceAddress Address { get; set; } = new();
        [JsonPropertyName("lineItems")] public List<LexwareInvoiceLineItem> LineItems { get; set; } = new();

        [JsonPropertyName("totalPrice")] public LexwareTotalPrice TotalPrice { get; set; } = new();
        [JsonPropertyName("taxConditions")] public LexwareTaxConditions TaxConditions { get; set; } = new();
        [JsonPropertyName("paymentConditions")] public LexwarePaymentConditions? PaymentConditions { get; set; }

        [JsonPropertyName("shippingConditions")] public LexwareShippingConditions ShippingConditions { get; set; } = new();

        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("introduction")] public string? Introduction { get; set; }
        [JsonPropertyName("remark")] public string? Remark { get; set; }
    }

    public class LexwareInvoiceAddress
    {
        [JsonPropertyName("contactId")] public string? ContactId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("street")] public string? Street { get; set; }
        [JsonPropertyName("zip")] public string? Zip { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
        [JsonPropertyName("supplement")] public string? Supplement { get; set; }
    }

    public class LexwareInvoiceLineItem
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "custom";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("quantity")] public decimal? Quantity { get; set; } = 1;
        [JsonPropertyName("unitName")] public string? UnitName { get; set; } = "Stück";
        [JsonPropertyName("unitPrice")] public LexwareUnitPrice? UnitPrice { get; set; }
        [JsonPropertyName("discountPercentage")] public decimal? DiscountPercentage { get; set; } = 0;
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    public class LexwareUnitPrice
    {
        [JsonPropertyName("currency")] public string Currency { get; set; } = "EUR";
        [JsonPropertyName("netAmount")] public decimal? NetAmount { get; set; }
        [JsonPropertyName("grossAmount")] public decimal? GrossAmount { get; set; }
        [JsonPropertyName("taxRatePercentage")] public decimal TaxRatePercentage { get; set; }
    }

    public class LexwareTotalPrice
    {
        [JsonPropertyName("currency")] public string Currency { get; set; } = "EUR";
    }

    public class LexwareTaxConditions
    {
        [JsonPropertyName("taxType")] public string TaxType { get; set; } = "net";
    }

    public class LexwarePaymentConditions
    {
        [JsonPropertyName("paymentTermLabel")] public string PaymentTermLabel { get; set; } = "Zahlbar sofort";
        [JsonPropertyName("paymentTermDuration")] public int PaymentTermDuration { get; set; } = 0;
    }

    public class LexwareShippingConditions
    {
        [JsonPropertyName("shippingType")] public string ShippingType { get; set; } = "service";

        // ✅ FIX: same datetime formatting rule
        [JsonPropertyName("shippingDate")]
        [JsonConverter(typeof(LexwareDateTimeOffsetConverter))]
        public DateTimeOffset ShippingDate { get; set; }

        [JsonPropertyName("shippingEndDate")]
        [JsonConverter(typeof(LexwareNullableDateTimeOffsetConverter))]
        public DateTimeOffset? ShippingEndDate { get; set; }
    }
}

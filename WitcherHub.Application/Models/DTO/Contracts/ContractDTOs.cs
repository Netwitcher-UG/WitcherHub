using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Contracts
{
    public class ContractDTOs
    {
        public ContractDto Contract { get; set; } = new();
        public List<ContractItemDto> Items { get; set; } = new();
        
    }

    public class ContractDto
    {
        public Guid ProjectId { get; set; }

        [System.ComponentModel.DataAnnotations.MaxLength(10)]
        public string Currency { get; set; } = "EUR";

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public ContractStructuredTermsDto? TermsStructured { get; set; }
        public string? Terms { get; set; }

        public DateTimeOffset? SignedAt { get; set; }
    }

    public class ContractItemDto
    {
        public Guid? ServiceId { get; set; }
        public string Title { get; set; } = "";

        public string Description { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;

        public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; } = 0m;
        public BillingCycle BillingCycle { get; set; } = BillingCycle.OneTime;

        public DiscountType? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }

        public List<Guid> PricingRuleIds { get; set; } = new();

        public decimal? AgreedPrice { get; set; }
        public int Position { get; set; } = 1;
    }

    public class UpdateContractDto
    {
        public ContractDto Contract { get; set; } = new();
        public List<ContractItemDto>? Items { get; set; } = new();
    }

    // Item ops
    public class CreateContractItemDto
    {
        public Guid ContractId { get; set; }
        public ContractItemDto Item { get; set; } = new();
    }

    public class UpdateContractItemDto
    {
        public Guid ContractId { get; set; }
        public Guid ItemId { get; set; }
        public ContractItemDto Item { get; set; } = new();
    }

    public class DeleteContractItemDto
    {
        public Guid ContractId { get; set; }
        public Guid ItemId { get; set; }
    }

    public class ReorderContractItemsDto
    {
        public Guid ContractId { get; set; }
        public List<Guid> OrderedItemIds { get; set; } = new();
    }
}

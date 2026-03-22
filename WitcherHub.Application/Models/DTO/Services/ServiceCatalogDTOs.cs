using System;
using System.Collections.Generic;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Services
{
    public class ServiceCatalogDTOs
    {
        public ServiceCatalogItemDto Service { get; set; } = new();
        public List<PricingRuleDto> PricingRules { get; set; } = new();
    }

    public class ServiceCatalogItemDto
    {
        public string Name { get; set; } = "";

        public ServiceType ServiceType { get; set; } = ServiceType.Other;
        public PricingModel PricingModel { get; set; } = PricingModel.Fixed;

        public decimal BasePrice { get; set; }
        public string DefaultCurrency { get; set; } = "EUR";
        public bool IsActive { get; set; } = true;

        // Optional
        public string DefaultUnitName { get; set; } = "";

        // Required by business rule
        public string DefaultDescription { get; set; } = "";

        public string? ConfigSchemaJson { get; set; }
    }

    public class PricingRuleDto
    {
        public string Name { get; set; } = "";

        public int Priority { get; set; } = 100;

        public string ConditionExpr { get; set; } = "true";
        public RuleAction Action { get; set; }
        public string ValueExpr { get; set; } = "0";

        public string? Label { get; set; }
        public string Scope { get; set; } = "LINE_ITEM";
        public bool IsActive { get; set; } = true;

        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }

    public class UpdateServiceCatalogItemDto
    {
        public ServiceCatalogItemDto Service { get; set; } = new();
    }

    public class CreatePricingRuleDto
    {
        public Guid ServiceId { get; set; }
        public PricingRuleDto Rule { get; set; } = new();
    }

    public class UpdatePricingRuleDto
    {
        public Guid ServiceId { get; set; }
        public Guid RuleId { get; set; }
        public PricingRuleDto Rule { get; set; } = new();
    }

    public class DeletePricingRuleDto
    {
        public Guid ServiceId { get; set; }
        public Guid RuleId { get; set; }
    }
}

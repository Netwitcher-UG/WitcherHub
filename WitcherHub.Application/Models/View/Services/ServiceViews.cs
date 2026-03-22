using System;
using System.Collections.Generic;
using System.Text.Json;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Services
{
    public class ServiceViews
    {
        public class ServiceListItemView
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";

            public ServiceType ServiceType { get; set; }
            public PricingModel PricingModel { get; set; }

            public decimal BasePrice { get; set; }
            public string DefaultCurrency { get; set; } = "EUR";
            public bool IsActive { get; set; }

            public int RulesCount { get; set; }
        }

        public class ServiceDetailsView
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";

            public ServiceType ServiceType { get; set; }
            public PricingModel PricingModel { get; set; }

            public decimal BasePrice { get; set; }
            public string DefaultCurrency { get; set; } = "EUR";
            public bool IsActive { get; set; }

            public string DefaultUnitName { get; set; } = "";
            public string DefaultDescription { get; set; } = "";

            public JsonDocument? ConfigSchema { get; set; }

            public List<PricingRuleItemView> PricingRules { get; set; } = new();
        }

        public class PricingRuleItemView
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";

            public int Priority { get; set; }
            public string ConditionExpr { get; set; } = "true";
            public RuleAction Action { get; set; }
            public string ValueExpr { get; set; } = "0";

            public string? Label { get; set; }
            public string Scope { get; set; } = "LINE_ITEM";
            public bool IsActive { get; set; }

            public DateOnly? ValidFrom { get; set; }
            public DateOnly? ValidTo { get; set; }
        }
    }
}

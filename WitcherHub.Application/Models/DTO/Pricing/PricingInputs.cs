using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Application.Models.DTO.Pricing
{

    public record PricingRuleDto(
        string Name,
        int Priority,
        string ConditionExpr,
        string Action,     // نخليها string حتى ما نربط Application بـ Enums infrastructure
        string ValueExpr,
        string? Label,
        string Scope,
        bool IsActive
    );

    public record ServicePricingDto(
        string Name,
        string PricingModel,   // "Fixed" / "Unit" / "Tiered" / "Hourly"
        decimal BasePrice,
        string DefaultCurrency,
        IReadOnlyList<PricingRuleDto> Rules
    );

    public record TaxRateDto(
        string Name,
        decimal RatePercent
    );

    public record ItemDiscountDto(
        string Type,    // "Percent" / "Amount" / "Fixed"
        decimal Value
    );
}

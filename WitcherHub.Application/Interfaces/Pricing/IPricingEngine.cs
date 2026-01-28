using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Models.DTO.Pricing;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces.Pricing
{

    public interface IPricingEngine
    {
        PriceBreakdownDto CalculateForService(
        ServicePricingDto service,
        decimal quantity,
        string currency,
        JsonDocument config,
        ItemDiscountDto? itemDiscount,
        TaxRateDto? taxRate,
        DateOnly? pricingDate = null
    );
    }
}

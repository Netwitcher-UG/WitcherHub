using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Application.Models.DTO.Pricing
{

    public record PriceLineDto(string Type, string Name, decimal Amount);

    public record PriceBreakdownDto(
        string Currency,
        decimal Base,
        decimal Adjustments,
        decimal Discounts,
        decimal Tax,
        decimal Total,
        IReadOnlyList<PriceLineDto> Lines
    );
}

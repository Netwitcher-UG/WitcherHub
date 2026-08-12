using System.Text.Json;
using WitcherHub.Application.Interfaces.Pricing;
using WitcherHub.Application.Models.DTO.Pricing;

namespace WitcherHub.Infrastructure.Services.Pricing;

public class PricingEngine : IPricingEngine
{
    public PriceBreakdownDto CalculateForService(
        ServicePricingDto service,
        decimal quantity,
        string currency,
        JsonDocument config,
        ItemDiscountDto? itemDiscount,
        TaxRateDto? taxRate,
        DateOnly? pricingDate = null)
    {
        pricingDate ??= DateOnly.FromDateTime(DateTime.UtcNow);

        var vars = BuildVars(service, quantity, currency, config);

        var lines = new List<PriceLineDto>();

        // 1) Base حسب PricingModel
        var baseAmount = Round(CalcBase(service, quantity, vars));
        vars["base"] = baseAmount;
        lines.Add(new("base", "Base", baseAmount));

        // 2) LINE_ITEM rules
        decimal adjustments = 0m;
        decimal discounts = 0m;

        foreach (var rule in service.Rules
                     .Where(r => r.IsActive)
                     .Where(r => r.Scope == "LINE_ITEM")
                     .OrderBy(r => r.Priority))
        {
            bool ok;
            try { ok = SimpleExpressionEvaluator.EvalBool(rule.ConditionExpr, vars); }
            catch { continue; }

            if (!ok) continue;

            decimal v;
            try { v = SimpleExpressionEvaluator.EvalDecimal(rule.ValueExpr, vars); }
            catch { continue; }

            ApplyRule(rule, v, ref adjustments, ref discounts, vars, lines, baseAmount);
        }

        // 3) Item-level discount (من QuoteItem/InvoiceItem)
        var itemDisc = CalcItemDiscount(itemDiscount, baseAmount + adjustments);
        if (itemDisc > 0)
        {
            discounts += itemDisc;
            lines.Add(new("discount", "Item Discount", -Round(itemDisc)));
        }

        // 4) Tax
        var taxable = Round((baseAmount + adjustments) - discounts);
        var tax = CalcTax(taxRate, taxable, lines);

        var total = Round(taxable + tax);

        return new PriceBreakdownDto(
            Currency: currency,
            Base: baseAmount,
            Adjustments: Round(adjustments),
            Discounts: Round(discounts),
            Tax: Round(tax),
            Total: total,
            Lines: lines
        );
    }

    private static Dictionary<string, object?> BuildVars(ServicePricingDto service, decimal qty, string currency, JsonDocument config)
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["qty"] = qty,
            ["quantity"] = qty,
            ["currency"] = currency,
            ["basePrice"] = service.BasePrice
        };

        if (config.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in config.RootElement.EnumerateObject())
            {
                vars[p.Name] = p.Value; // JsonElement
            }
        }

        return vars;
    }

    private static decimal CalcBase(ServicePricingDto service, decimal qty, Dictionary<string, object?> vars)
    {
        var model = (service.PricingModel ?? "Fixed").ToUpperInvariant();

        return model switch
        {
            "FIXED" => service.BasePrice,
            "UNIT" => service.BasePrice * qty,
            "HOURLY" => service.BasePrice * ReadDec(vars, "hours", fallback: qty),
            "TIERED" => service.BasePrice * qty, // نسخة 1 مؤقتة
            _ => service.BasePrice * qty
        };
    }

    private static decimal ReadDec(Dictionary<string, object?> vars, string key, decimal fallback)
    {
        if (!vars.TryGetValue(key, out var v) || v is null) return fallback;

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d)) return d;
        if (v is decimal dd) return dd;
        return fallback;
    }

    private static void ApplyRule(
        PricingRuleDto rule,
        decimal value,
        ref decimal adjustments,
        ref decimal discounts,
        Dictionary<string, object?> vars,
        List<PriceLineDto> lines,
        decimal baseAmount)
    {
        var a = (rule.Action ?? "").ToUpperInvariant();

        decimal currentSubtotal = baseAmount + adjustments;
        vars["subtotal"] = currentSubtotal;
        vars["total"] = currentSubtotal - discounts;

        // discount percent if action contains PERCENT
        if (a.Contains("DISCOUNT") && a.Contains("PERCENT"))
        {
            var disc = Round(currentSubtotal * (value / 100m));
            disc = Clamp(disc, currentSubtotal - discounts);
            discounts += disc;
            lines.Add(new("discount", rule.Label ?? rule.Name, -disc));
            return;
        }

        // discount amount
        if (a.Contains("DISCOUNT"))
        {
            var disc = Clamp(Round(value), currentSubtotal - discounts);
            discounts += disc;
            lines.Add(new("discount", rule.Label ?? rule.Name, -disc));
            return;
        }

        // default add/adjustment
        var add = Round(value);
        adjustments += add;
        lines.Add(new("adjustment", rule.Label ?? rule.Name, add));
    }

    private static decimal CalcItemDiscount(ItemDiscountDto? d, decimal amount)
    {
        if (d is null) return 0m;

        var type = (d.Type ?? "").ToUpperInvariant();

        // Percent values are stored as 0-100, the same convention the quote and
        // contract calculators use. The previous "value <= 1 is already a
        // fraction" heuristic silently turned a 0.5% discount into 50%.
        var raw = type switch
        {
            "PERCENT" => amount * (d.Value / 100m),
            "AMOUNT" => d.Value,
            "FIXED" => d.Value,
            _ => 0m
        };

        return Clamp(raw, amount);
    }

    /// <summary>
    /// A discount can never be negative, nor larger than the amount it applies to.
    /// </summary>
    private static decimal Clamp(decimal discount, decimal maximum) =>
        Math.Min(Math.Max(0m, maximum), Math.Max(0m, discount));

    private static decimal CalcTax(TaxRateDto? taxRate, decimal taxable, List<PriceLineDto> lines)
    {
        if (taxRate is null) return 0m;

        var tax = Round(taxable * (taxRate.RatePercent / 100m));
        if (tax > 0) lines.Add(new("tax", taxRate.Name, tax));
        return tax;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

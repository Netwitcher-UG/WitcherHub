using System.Text.Json;
using WitcherHub.Application.Models.DTO.Pricing;
using WitcherHub.Infrastructure.Services.Pricing;

namespace WitcherHub.Tests;

public class PricingEngineTests
{
    private static readonly JsonDocument EmptyConfig = JsonDocument.Parse("{}");

    private static ServicePricingDto Service(
        string pricingModel = "Fixed",
        decimal basePrice = 100m,
        params PricingRuleDto[] rules) =>
        new("Test service", pricingModel, basePrice, "EUR", rules);

    [Theory]
    [InlineData("Fixed", 100, 3, 100)]
    [InlineData("Unit", 100, 3, 300)]
    [InlineData("Hourly", 50, 4, 200)]
    public void CalculatesBaseAmountPerPricingModel(
        string model, decimal basePrice, decimal quantity, decimal expected)
    {
        var result = new PricingEngine().CalculateForService(
            Service(model, basePrice), quantity, "EUR", EmptyConfig, null, null);

        Assert.Equal(expected, result.Base);
    }

    [Theory]
    [InlineData(0.5, 0.50)]   // half a percent, not half the price
    [InlineData(1, 1.00)]
    [InlineData(10, 10.00)]
    [InlineData(100, 100.00)]
    public void PercentDiscountIsAlwaysOutOfOneHundred(decimal percent, decimal expectedDiscount)
    {
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 100m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: new ItemDiscountDto("Percent", percent),
            taxRate: null);

        Assert.Equal(expectedDiscount, result.Discounts);
        Assert.Equal(100m - expectedDiscount, result.Total);
    }

    [Fact]
    public void DiscountNeverExceedsTheAmountItAppliesTo()
    {
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 100m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: new ItemDiscountDto("Amount", 500m),
            taxRate: null);

        Assert.Equal(100m, result.Discounts);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void NegativeDiscountIsIgnoredRatherThanIncreasingThePrice()
    {
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 100m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: new ItemDiscountDto("Amount", -50m),
            taxRate: null);

        Assert.Equal(0m, result.Discounts);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public void TaxAppliesToTheDiscountedAmount()
    {
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 200m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: new ItemDiscountDto("Percent", 10m),
            taxRate: new TaxRateDto("USt.", 19m));

        Assert.Equal(20m, result.Discounts);
        Assert.Equal(34.20m, result.Tax);       // 19% of 180
        Assert.Equal(214.20m, result.Total);
    }

    [Fact]
    public void NoTaxLineIsEmittedWhenTheRateIsZero()
    {
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 100m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: null,
            taxRate: new TaxRateDto("USt.", 0m));

        Assert.Equal(0m, result.Tax);
        Assert.DoesNotContain(result.Lines, l => l.Type == "tax");
    }

    [Fact]
    public void NoDiscountLineIsEmittedWhenThereIsNoDiscount()
    {
        // Guards the "-0,00" rendering defect: a zero discount must not become a line.
        var result = new PricingEngine().CalculateForService(
            Service(basePrice: 100m),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: new ItemDiscountDto("Percent", 0m),
            taxRate: null);

        Assert.Equal(0m, result.Discounts);
        Assert.DoesNotContain(result.Lines, l => l.Type == "discount");
    }

    [Fact]
    public void HourlyPricingReadsHoursFromTheItemConfig()
    {
        using var config = JsonDocument.Parse("""{"hours": 7.5}""");

        var result = new PricingEngine().CalculateForService(
            Service("Hourly", 80m),
            quantity: 1m,
            currency: "EUR",
            config: config,
            itemDiscount: null,
            taxRate: null);

        Assert.Equal(600m, result.Base);
    }

    [Fact]
    public void PercentRuleDiscountUsesTheSameHundredBasedConvention()
    {
        var rule = new PricingRuleDto(
            Name: "Loyalty",
            Priority: 1,
            ConditionExpr: "true",
            Action: "DISCOUNT_PERCENT",
            ValueExpr: "5",
            Label: "Loyalty discount",
            Scope: "LINE_ITEM",
            IsActive: true);

        var result = new PricingEngine().CalculateForService(
            Service("Fixed", 100m, rule),
            quantity: 1m,
            currency: "EUR",
            config: EmptyConfig,
            itemDiscount: null,
            taxRate: null);

        Assert.Equal(5m, result.Discounts);
        Assert.Equal(95m, result.Total);
    }
}

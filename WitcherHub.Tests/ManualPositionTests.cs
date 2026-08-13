using FluentValidation;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Validators.Contract;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

public class ManualPositionTests
{
    private static readonly ManualPositionValidator Validator = new();

    private static ManualPositionDto Position(Action<ManualPositionDto>? configure = null)
    {
        var dto = new ManualPositionDto
        {
            Title = "Website relaunch",
            Quantity = 1,
            UnitPrice = 1000m,
            Currency = "EUR",
            PricingModel = PricingModel.Fixed,
            Position = 1
        };

        configure?.Invoke(dto);
        return dto;
    }

    // ---- a manual position needs no catalog service ------------------------

    [Fact]
    public void AManualPositionIsValidWithNoCatalogServiceAtAll()
    {
        var result = Validator.Validate(Position(p => p.CatalogServiceId = null));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void AManualPositionMayNotReferenceACatalogService()
    {
        // Guards against reintroducing a placeholder catalog record for manual work.
        var result = Validator.Validate(Position(p =>
        {
            p.SourceType = ContractItemSource.Manual;
            p.CatalogServiceId = Guid.NewGuid();
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ManualPositionDto.CatalogServiceId));
    }

    [Fact]
    public void ACatalogPositionMustReferenceAService()
    {
        var result = Validator.Validate(Position(p =>
        {
            p.SourceType = ContractItemSource.Catalog;
            p.CatalogServiceId = null;
        }));

        Assert.False(result.IsValid);
    }

    // ---- price and currency are mandatory unless explicitly free -----------

    [Fact]
    public void APriceIsRequiredUnlessThePositionIsMarkedFree()
    {
        var result = Validator.Validate(Position(p => p.UnitPrice = null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("free", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFreePositionNeedsNoPrice()
    {
        var result = Validator.Validate(Position(p =>
        {
            p.IsFree = true;
            p.UnitPrice = null;
        }));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void AFreePositionCannotAlsoCarryAPrice()
    {
        var result = Validator.Validate(Position(p =>
        {
            p.IsFree = true;
            p.UnitPrice = 500m;
        }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ACurrencyIsRequiredForAPricedPosition()
    {
        var result = Validator.Validate(Position(p => p.Currency = ""));

        Assert.False(result.IsValid);
    }

    // ---- recurring cycles need a term --------------------------------------

    [Theory]
    [InlineData(BillingCycle.Monthly)]
    [InlineData(BillingCycle.Quarterly)]
    [InlineData(BillingCycle.Annual)]
    public void ARecurringPositionMustStateHowManyPeriodsWereAgreed(BillingCycle cycle)
    {
        var result = Validator.Validate(Position(p =>
        {
            p.BillingCycle = cycle;
            p.DurationPeriods = null;
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ManualPositionDto.DurationPeriods));
    }

    [Fact]
    public void AOneTimePositionNeedsNoPeriodCount()
    {
        var result = Validator.Validate(Position(p => p.BillingCycle = BillingCycle.OneTime));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ActivationOnASpecifiedDateRequiresThatDate()
    {
        var result = Validator.Validate(Position(p =>
        {
            p.ActivationMethod = ActivationMethod.OnSpecifiedDate;
            p.StartDate = null;
        }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DeliveryCannotPrecedeTheStartDate()
    {
        var result = Validator.Validate(Position(p =>
        {
            p.StartDate = new DateOnly(2026, 5, 1);
            p.DeliveryDate = new DateOnly(2026, 4, 1);
        }));

        Assert.False(result.IsValid);
    }

    // ---- money -------------------------------------------------------------

    [Fact]
    public void AFixedPriceIsTheLineTotalRegardlessOfQuantity()
    {
        var p = Position(x =>
        {
            x.PricingModel = PricingModel.Fixed;
            x.UnitPrice = 2500m;
            x.Quantity = 4;
        });

        Assert.Equal(2500m, p.NetTotal);
    }

    [Fact]
    public void AUnitPriceIsMultipliedByQuantity()
    {
        var p = Position(x =>
        {
            x.PricingModel = PricingModel.Unit;
            x.UnitPrice = 120m;
            x.Quantity = 7;
        });

        Assert.Equal(840m, p.NetTotal);
    }

    [Fact]
    public void APercentageDiscountComesOffTheLineTotal()
    {
        var p = Position(x =>
        {
            x.PricingModel = PricingModel.Unit;
            x.UnitPrice = 100m;
            x.Quantity = 10;
            x.DiscountType = DiscountType.Percent;
            x.DiscountValue = 15m;
        });

        Assert.Equal(850m, p.NetTotal);
    }

    [Fact]
    public void ADiscountCannotExceedTheLineTotal()
    {
        var p = Position(x =>
        {
            x.UnitPrice = 200m;
            x.DiscountType = DiscountType.Amount;
            x.DiscountValue = 900m;
        });

        Assert.Equal(0m, p.NetTotal);
    }

    [Fact]
    public void VatIsCalculatedOnTheDiscountedAmount()
    {
        var p = Position(x =>
        {
            x.UnitPrice = 1000m;
            x.DiscountType = DiscountType.Percent;
            x.DiscountValue = 10m;
            x.VatRate = 19m;
        });

        Assert.Equal(900m, p.NetTotal);
        Assert.Equal(171m, p.VatAmount);
        Assert.Equal(1071m, p.GrossTotal);
    }

    [Fact]
    public void AFreePositionContributesNothing()
    {
        var p = Position(x =>
        {
            x.IsFree = true;
            x.UnitPrice = null;
            x.VatRate = 19m;
        });

        Assert.Equal(0m, p.NetTotal);
        Assert.Equal(0m, p.VatAmount);
        Assert.Equal(0m, p.GrossTotal);
    }

    // ---- totals across a contract -----------------------------------------

    [Fact]
    public void TotalsAggregateEveryPosition()
    {
        var positions = new List<ManualPositionDto>
        {
            Position(p => { p.UnitPrice = 1000m; p.VatRate = 19m; }),
            Position(p =>
            {
                p.Title = "Hosting";
                p.PricingModel = PricingModel.Unit;
                p.UnitPrice = 50m;
                p.Quantity = 12;
                p.VatRate = 19m;
                p.BillingCycle = BillingCycle.Monthly;
                p.DurationPeriods = 12;
            }),
            Position(p => { p.Title = "Onboarding"; p.IsFree = true; p.UnitPrice = null; })
        };

        var totals = PositionTotalsDto.From(positions);

        Assert.Equal(3, totals.PositionCount);
        Assert.Equal(1600m, totals.Subtotal);          // 1000 + 600 + 0
        Assert.Equal(304m, totals.Vat);                // 19% of 1600
        Assert.Equal(1904m, totals.Total);
        Assert.Equal("EUR", totals.Currency);
    }

    [Fact]
    public void TotalsReportTheDiscountSeparately()
    {
        var positions = new List<ManualPositionDto>
        {
            Position(p =>
            {
                p.UnitPrice = 1000m;
                p.DiscountType = DiscountType.Percent;
                p.DiscountValue = 20m;
            })
        };

        var totals = PositionTotalsDto.From(positions);

        Assert.Equal(200m, totals.Discount);
        Assert.Equal(800m, totals.Subtotal);
    }

    [Fact]
    public void TotalsOfAnEmptyContractAreZeroRatherThanAnError()
    {
        var totals = PositionTotalsDto.From(Array.Empty<ManualPositionDto>());

        Assert.Equal(0, totals.PositionCount);
        Assert.Equal(0m, totals.Total);
    }
}

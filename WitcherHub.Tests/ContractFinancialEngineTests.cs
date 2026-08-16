using WitcherHub.Domain.Commercial;

namespace WitcherHub.Tests;

/// <summary>
/// The deterministic financial engine.
///
/// Every scenario here is synthetic and from a different line of business —
/// haulage, laboratory testing, licensing, construction, catering, translation —
/// on purpose. Nothing in this file resembles any contract the application has
/// processed, because a financial engine that only adds up the shapes it has
/// already seen is not an engine, it is a transcription of one customer.
///
/// The single idea being tested throughout: only money the customer is certain
/// to owe reaches the committed total, and everything else is reported as what
/// it is rather than added, dropped, or treated as zero.
/// </summary>
public class ContractFinancialEngineTests
{
    private static CommercialTerm Term(string name) => new() { Name = name };

    // ================================================================ one-off

    [Fact]
    public void A_single_agreed_fee_is_committed()
    {
        var terms = new[]
        {
            Term("Structural survey") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(8_400m, "GBP", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms, "GBP");

        Assert.Equal(8_400m, money.CommittedOneTime);
        Assert.Equal(8_400m, money.CommittedNet);
        Assert.Empty(money.Unresolved);
        Assert.Equal("GBP", money.Currency);
    }

    // ============================================================== recurring

    [Fact]
    public void A_recurring_charge_with_a_stated_term_is_totalled_across_it()
    {
        var terms = new[]
        {
            Term("Cold storage, pallet space") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(1_250m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                DurationCount = 18,
                DurationUnit = PeriodUnit.Month,
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(22_500m, money.CommittedRecurringTotal);      // 1250 × 18
        Assert.Equal(1_250m, money.CommittedMonthlyEquivalent);
        Assert.Equal(0m, money.CommittedOneTime);
        Assert.Empty(money.Unresolved);
    }

    [Fact]
    public void A_recurring_charge_with_no_end_gives_a_monthly_figure_and_no_total()
    {
        var terms = new[]
        {
            Term("Licence, per seat") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(19m, "USD", Commitment.Committed),
                Quantity = 40,
                BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms, "USD");

        // An open-ended subscription has no contract total. Inventing a horizon
        // to produce one would state a commitment nobody made.
        Assert.Equal(0m, money.CommittedRecurringTotal);
        Assert.Equal(760m, money.CommittedMonthlyEquivalent);
        Assert.Single(money.Unresolved);
        Assert.Contains("no end date", money.Unresolved[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PeriodUnit.Week, 1, 12, 52)]        // 12 months ≈ 52.18 weeks
    [InlineData(PeriodUnit.Week, 2, 12, 26)]        // fortnightly
    [InlineData(PeriodUnit.Quarter, 1, 12, 4)]
    [InlineData(PeriodUnit.Year, 1, 24, 2)]
    [InlineData(PeriodUnit.Month, 3, 12, 4)]        // every third month
    public void Arbitrary_billing_intervals_are_counted_correctly(
        PeriodUnit unit, int interval, int months, int expectedOccurrences)
    {
        var terms = new[]
        {
            Term("Calibration visit") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(100m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(unit, interval),
                DurationCount = months,
                DurationUnit = PeriodUnit.Month,
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        // Whole units of 100, so the count is readable straight off the total.
        Assert.Equal(expectedOccurrences, (int)Math.Round(money.CommittedRecurringTotal / 100m));
    }

    // =============================================================== variable

    [Fact]
    public void An_hourly_rate_with_no_committed_hours_is_not_committed_money()
    {
        var terms = new[]
        {
            Term("Expert witness time") with
            {
                PricingModel = PricingModelKind.TimeAndMaterials,
                UnitRate = new MoneyAmount(240m, "GBP"),
                QuantityUnit = "hour",
                Commitment = Commitment.Variable
            }
        };

        var money = ContractFinancialEngine.Calculate(terms, "GBP");

        // The rate is perfectly definite. The amount is not, and the difference
        // is the whole point.
        Assert.Equal(0m, money.CommittedNet);
        Assert.Single(money.Unresolved);
        Assert.Contains("quantity", money.Unresolved[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Usage_based_charges_stay_out_of_the_committed_total()
    {
        var terms = new[]
        {
            Term("Per-tonne handling") with
            {
                PricingModel = PricingModelKind.UsageBased,
                UnitRate = new MoneyAmount(18.50m, "EUR"),
                QuantityUnit = "tonne",
                BillingRecurrence = Recurrence.PerUsage(),
                Commitment = Commitment.Variable
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(0m, money.CommittedNet);
        Assert.True(money.HasNoCommittedValue);
    }

    [Fact]
    public void A_minimum_commitment_alongside_usage_is_the_part_that_counts()
    {
        var terms = new[]
        {
            Term("Minimum monthly volume") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(2_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                DurationCount = 12,
                DurationUnit = PeriodUnit.Month,
                Commitment = Commitment.Committed
            },
            Term("Volume above the minimum") with
            {
                PricingModel = PricingModelKind.UsageBased,
                UnitRate = new MoneyAmount(0.35m, "EUR"),
                QuantityUnit = "unit",
                Commitment = Commitment.Variable
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(24_000m, money.CommittedNet);
        Assert.Single(money.Unresolved);
    }

    // =============================================================== optional

    [Fact]
    public void An_optional_item_is_reported_separately_however_firm_its_price()
    {
        var terms = new[]
        {
            Term("Additional site, if taken") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(3_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                IsMandatory = false,                 // overrides the commitment
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(0m, money.CommittedNet);
        Assert.Equal(3_000m, money.Optional);
    }

    [Fact]
    public void An_estimate_is_never_treated_as_a_commitment()
    {
        var terms = new[]
        {
            Term("Anticipated print run") with
            {
                PricingModel = PricingModelKind.PerUnit,
                UnitRate = new MoneyAmount(0.12m, "EUR"),
                Quantity = 50_000m,
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Estimated
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(0m, money.CommittedNet);
        Assert.Equal(6_000m, money.Estimated);
    }

    // ================================================================= phases

    [Fact]
    public void A_price_that_changes_partway_through_is_summed_by_phase()
    {
        var terms = new[]
        {
            Term("Managed service") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                Commitment = Commitment.Committed,
                Phases = new[]
                {
                    new PricingPhase
                    {
                        Label = "Introductory rate",
                        Sequence = 1,
                        Rate = new MoneyAmount(1_500m, "EUR"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                        DurationCount = 4,
                        DurationUnit = PeriodUnit.Month
                    },
                    new PricingPhase
                    {
                        Label = "Standard rate",
                        Sequence = 2,
                        Rate = new MoneyAmount(2_200m, "EUR"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                        DurationCount = 8,
                        DurationUnit = PeriodUnit.Month
                    }
                }
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        // 4 × 1500 + 8 × 2200. Not 12 × an average, which is a figure the
        // agreement never states.
        Assert.Equal(23_600m, money.CommittedRecurringTotal);

        // The monthly equivalent is what it runs at once the phases have passed.
        Assert.Equal(2_200m, money.CommittedMonthlyEquivalent);
    }

    [Fact]
    public void Phases_bounded_by_a_condition_rather_than_a_date_are_not_guessed_at()
    {
        var terms = new[]
        {
            Term("Support, rate changes at go-live") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                Commitment = Commitment.Committed,
                Phases = new[]
                {
                    new PricingPhase
                    {
                        Label = "Before go-live",
                        Sequence = 1,
                        Rate = new MoneyAmount(900m, "EUR"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                        EndCondition = "until the system goes live"
                    },
                    new PricingPhase
                    {
                        Label = "After go-live",
                        Sequence = 2,
                        Rate = new MoneyAmount(1_400m, "EUR"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                        StartCondition = "from go-live"
                    }
                }
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        // Neither phase has a length, so neither can be totalled — and the
        // condition is preserved rather than converted into an assumed date.
        Assert.Equal(0m, money.CommittedRecurringTotal);
        Assert.NotEmpty(money.Unresolved);
        Assert.Equal("until the system goes live", terms[0].Phases[0].EndCondition);
    }

    [Fact]
    public void Phases_measured_in_quarters_and_years_convert_without_special_cases()
    {
        var terms = new[]
        {
            Term("Multi-year retainer") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                Commitment = Commitment.Committed,
                Phases = new[]
                {
                    new PricingPhase
                    {
                        Sequence = 1,
                        Rate = new MoneyAmount(6_000m, "CHF"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Quarter),
                        DurationCount = 1,
                        DurationUnit = PeriodUnit.Year
                    },
                    new PricingPhase
                    {
                        Sequence = 2,
                        Rate = new MoneyAmount(7_500m, "CHF"),
                        BillingRecurrence = Recurrence.Every(PeriodUnit.Year),
                        DurationCount = 2,
                        DurationUnit = PeriodUnit.Year
                    }
                }
            }
        };

        var money = ContractFinancialEngine.Calculate(terms, "CHF");

        // 4 quarters × 6000, then 2 years × 7500.
        Assert.Equal(39_000m, money.CommittedRecurringTotal);
    }

    // ============================================================== discounts

    [Fact]
    public void A_percentage_discount_reduces_the_committed_amount()
    {
        var terms = new[]
        {
            Term("Annual subscription") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(10_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                DiscountPercent = 15m,
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(8_500m, money.CommittedNet);
        Assert.Equal(1_500m, money.Discounts);
    }

    [Fact]
    public void A_credit_reduces_the_total_rather_than_adding_to_it()
    {
        var terms = new[]
        {
            Term("Installation") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(5_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Committed
            },
            Term("Goodwill credit") with
            {
                PricingModel = PricingModelKind.Credit,
                FixedAmount = new MoneyAmount(750m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(4_250m, money.CommittedNet);
    }

    // ==================================================================== tax

    [Fact]
    public void Tax_is_calculated_only_where_a_rate_is_stated()
    {
        var withRate = new[]
        {
            Term("Consultancy") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(1_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                TaxRatePercent = 20m,
                Commitment = Commitment.Committed
            }
        };

        Assert.Equal(200m, ContractFinancialEngine.Calculate(withRate).CommittedTax);

        var treatmentOnly = new[]
        {
            Term("Consultancy") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(1_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                TaxTreatment = "plus statutory sales tax",
                Commitment = Commitment.Committed
            }
        };

        var money = ContractFinancialEngine.Calculate(treatmentOnly);

        // A treatment without a rate is not a rate. Assuming the local standard
        // rate would put a number on the contract that the contract never stated.
        Assert.Equal(0m, money.CommittedTax);
        Assert.Contains(money.Unresolved, u => u.Reason.Contains("no rate", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================= custom

    [Fact]
    public void An_unrecognised_pricing_arrangement_is_kept_and_left_out_of_the_total()
    {
        var terms = new[]
        {
            Term("Revenue share above threshold, banded by region") with
            {
                PricingModel = PricingModelKind.Custom,
                CustomPricingModel = "3% of net revenue in each region above a per-region threshold, reconciled yearly",
                Commitment = Commitment.Variable
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        // Kept in full, not approximated to the nearest familiar model, and not
        // totalled on a basis nobody agreed.
        Assert.Equal(0m, money.CommittedNet);
        Assert.Single(money.Unresolved);
        Assert.Contains("custom", money.Unresolved[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3% of net revenue", terms[0].CustomPricingModel!);
    }

    // ================================================================== mixed

    [Fact]
    public void A_contract_mixing_every_kind_of_money_keeps_them_apart()
    {
        var terms = new[]
        {
            Term("Mobilisation") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(12_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Committed
            },
            Term("Site presence") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(4_000m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(PeriodUnit.Month),
                DurationCount = 6,
                DurationUnit = PeriodUnit.Month,
                Commitment = Commitment.Committed
            },
            Term("Overtime") with
            {
                PricingModel = PricingModelKind.TimeAndMaterials,
                UnitRate = new MoneyAmount(95m, "EUR"),
                QuantityUnit = "hour",
                Commitment = Commitment.Variable
            },
            Term("Weekend cover, if requested") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(1_800m, "EUR"),
                BillingRecurrence = Recurrence.Once(),
                IsMandatory = false,
                Commitment = Commitment.Committed
            },
            Term("Expected materials") with
            {
                PricingModel = PricingModelKind.FixedAmount,
                FixedAmount = new MoneyAmount(7_500m, "EUR"),
                BillingRecurrence = Recurrence.Once(),
                Commitment = Commitment.Estimated
            }
        };

        var money = ContractFinancialEngine.Calculate(terms);

        Assert.Equal(12_000m, money.CommittedOneTime);
        Assert.Equal(24_000m, money.CommittedRecurringTotal);
        Assert.Equal(36_000m, money.CommittedNet);
        Assert.Equal(7_500m, money.Estimated);
        Assert.Equal(1_800m, money.Optional);
        Assert.True(money.IsPartial);

        // Every bucket separate. One number for all of it would be a figure the
        // contract does not contain.
        Assert.NotEqual(money.CommittedNet, money.CommittedNet + money.Estimated + money.Optional);
    }

    [Fact]
    public void An_empty_contract_is_not_a_contract_worth_zero()
    {
        var money = ContractFinancialEngine.Calculate(Array.Empty<CommercialTerm>());

        Assert.Equal(0m, money.CommittedNet);

        // Nothing unresolved either: there is genuinely nothing here, which is a
        // different statement from "the value could not be worked out".
        Assert.Empty(money.Unresolved);
        Assert.False(money.IsPartial);
    }

    [Fact]
    public void The_same_terms_always_produce_the_same_figures()
    {
        var terms = new[]
        {
            Term("Anything") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                UnitRate = new MoneyAmount(333.33m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(PeriodUnit.Week, 2),
                DurationCount = 9,
                DurationUnit = PeriodUnit.Month,
                Commitment = Commitment.Committed
            }
        };

        var first = ContractFinancialEngine.Calculate(terms);
        var second = ContractFinancialEngine.Calculate(terms);

        // No clock, no randomness, no model. A total that cannot be reproduced
        // cannot be defended to a customer.
        //
        // Compared field by field rather than with record equality: the record
        // holds a list, and a list compares by reference, so Assert.Equal on the
        // whole thing fails for two identical results.
        Assert.Equal(first.CommittedOneTime, second.CommittedOneTime);
        Assert.Equal(first.CommittedRecurringTotal, second.CommittedRecurringTotal);
        Assert.Equal(first.CommittedMonthlyEquivalent, second.CommittedMonthlyEquivalent);
        Assert.Equal(first.CommittedNet, second.CommittedNet);
        Assert.Equal(first.CommittedTax, second.CommittedTax);
        Assert.Equal(first.Discounts, second.Discounts);
        Assert.Equal(
            first.Unresolved.Select(u => u.Reason),
            second.Unresolved.Select(u => u.Reason));
    }
}

using WitcherHub.Domain.Commercial;

namespace WitcherHub.Tests;

/// <summary>
/// Validation that keeps what it criticises.
///
/// The rule throughout: a term with a problem is a term with a problem, not a
/// term to throw away. Discarding an extraction because one field was wrong
/// means a person re-types everything that was right, and correcting a
/// contradiction automatically means choosing which of two stated figures the
/// customer agreed to — which is not a decision code gets to make.
/// </summary>
public class TermValidatorTests
{
    private static CommercialTerm Term(string name) => new()
    {
        Name = name,
        PricingModel = PricingModelKind.FixedAmount,
        FixedAmount = new MoneyAmount(100m, "EUR", Commitment.Committed),
        BillingRecurrence = Recurrence.Once(),
        Commitment = Commitment.Committed
    };

    [Fact]
    public void A_clean_term_produces_nothing_to_report()
    {
        var outcome = TermValidator.Validate(new[] { Term("Bunker survey") });

        Assert.Single(outcome.Terms);
        Assert.Empty(outcome.Issues);
        Assert.False(outcome.HasProblems);
    }

    [Fact]
    public void A_nameless_term_is_kept_so_its_figures_are_not_lost()
    {
        var outcome = TermValidator.Validate(new[] { Term("") with { Name = "" } });

        Assert.Single(outcome.Terms);
        Assert.Contains(outcome.Issues, i => i.Field == "Name" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void A_term_with_nothing_in_it_at_all_is_dropped_with_a_reason()
    {
        var outcome = TermValidator.Validate(new[] { new CommercialTerm() });

        Assert.Empty(outcome.Terms);
        Assert.Single(outcome.DiscardedReasons);
    }

    [Fact]
    public void Free_of_charge_carrying_a_price_is_a_contradiction_and_both_halves_survive()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Trial period") with { PricingModel = PricingModelKind.NoCharge }
        });

        // Neither the "free" nor the "100" is deleted to make the other true.
        Assert.Single(outcome.Terms);
        Assert.Equal(100m, outcome.Terms[0].FixedAmount!.Value);
        Assert.True(outcome.HasProblems);
    }

    [Fact]
    public void Dates_in_the_wrong_order_are_reported_and_left_as_read()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Seasonal cover") with
            {
                StartDate = new DateOnly(2028, 9, 1),
                EndDate = new DateOnly(2028, 3, 1)
            }
        });

        var term = Assert.Single(outcome.Terms);

        Assert.Equal(new DateOnly(2028, 9, 1), term.StartDate);
        Assert.Equal(new DateOnly(2028, 3, 1), term.EndDate);
        Assert.Contains(outcome.Issues, i => i.Severity == ValidationSeverity.Problem);
    }

    [Fact]
    public void A_cap_below_the_minimum_is_impossible_and_is_said_so()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Metered supply") with
            {
                MinimumCommitment = new MoneyAmount(5_000m, "EUR"),
                Cap = new MoneyAmount(2_000m, "EUR")
            }
        });

        Assert.Contains(outcome.Issues, i =>
            i.Severity == ValidationSeverity.Problem &&
            i.Message.Contains("cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_open_ended_recurring_charge_is_noted_as_uncalculable()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Standing charge") with
            {
                PricingModel = PricingModelKind.RecurringAmount,
                FixedAmount = null,
                UnitRate = new MoneyAmount(150m, "EUR", Commitment.Committed),
                BillingRecurrence = Recurrence.Every(PeriodUnit.Month)
            }
        });

        Assert.Contains(outcome.Issues, i =>
            i.Field == "Duration" && i.Message.Contains("no end date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Custom_pricing_with_no_description_records_nothing_and_says_so()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Something unusual") with { PricingModel = PricingModelKind.Custom }
        });

        Assert.Contains(outcome.Issues, i => i.Field == "CustomPricingModel");
    }

    [Fact]
    public void Overlapping_pricing_periods_are_reported_and_neither_is_moved()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Phased rate") with
            {
                Phases = new[]
                {
                    new PricingPhase
                    {
                        Sequence = 1,
                        Rate = new MoneyAmount(100m, "EUR"),
                        StartDate = new DateOnly(2028, 1, 1),
                        EndDate = new DateOnly(2028, 6, 30)
                    },
                    new PricingPhase
                    {
                        Sequence = 2,
                        Rate = new MoneyAmount(120m, "EUR"),
                        StartDate = new DateOnly(2028, 6, 1),
                        EndDate = new DateOnly(2028, 12, 31)
                    }
                }
            }
        });

        Assert.Contains(outcome.Issues, i => i.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, outcome.Terms[0].Phases.Count);
    }

    [Fact]
    public void A_phase_with_no_amount_is_flagged_and_the_term_survives()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Two-stage fee") with
            {
                Phases = new[]
                {
                    new PricingPhase { Sequence = 1, Rate = new MoneyAmount(500m, "EUR") },
                    new PricingPhase { Sequence = 2, Rate = MoneyAmount.NotStated() }
                }
            }
        });

        Assert.Single(outcome.Terms);
        Assert.Contains(outcome.Issues, i => i.Field == "Phases");
    }

    [Fact]
    public void Everything_valid_in_a_mixed_batch_comes_through_intact()
    {
        var outcome = TermValidator.Validate(new[]
        {
            Term("Fine"),
            Term("Also fine"),
            Term("Broken") with { PricingModel = PricingModelKind.NoCharge },
            new CommercialTerm()      // nothing at all
        });

        // Three kept, one dropped. The two good ones are untouched by the
        // company they were sent in.
        Assert.Equal(3, outcome.Terms.Count);
        Assert.Single(outcome.DiscardedReasons);
        Assert.Empty(outcome.For(outcome.Terms[0].Key));
    }
}

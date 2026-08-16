using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Domain.Commercial;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests;

/// <summary>
/// The extraction pipeline, exercised on commercial structures unlike anything
/// the application currently holds.
///
/// Every fixture here is synthetic and deliberately foreign: a shipping
/// agreement, a clinical trial, a franchise, a translation retainer. The point
/// is not that these particular documents work — it is that nothing in the code
/// under test knows what kind of business it is reading, so the ones nobody has
/// thought of will work too.
///
/// No test here reaches a real API.
/// </summary>
public class GenericExtractionTests
{
    private sealed class StubAi : IAiTextGenerator
    {
        private readonly Func<string, Task<string>> _respond;

        public StubAi(string response) => _respond = _ => Task.FromResult(response);
        public StubAi(Func<string, Task<string>> respond) => _respond = respond;

        public string? LastPrompt { get; private set; }

        public Task<string> GenerateTextAsync(string prompt)
        {
            LastPrompt = prompt;
            return _respond(prompt);
        }
    }

    private static SemanticContractAnalyzer Analyzer(IAiTextGenerator ai) =>
        new(ai,
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            NullLogger<SemanticContractAnalyzer>.Instance);

    // ============================================================ recurring vs one-time

    [Fact]
    public async Task Recurring_and_one_time_charges_are_told_apart_and_totalled_differently()
    {
        // A shipping agreement: a joining fee and a berth charge.
        const string response = """
            {
              "detectedLanguage": "en",
              "documentType": "Berth licence",
              "concepts": [],
              "terms": [
                { "key": "t1", "name": "Joining fee", "pricingModel": "FixedAmount",
                  "fixedAmount": 4500, "currency": "USD", "billingRecurrence": "one-time",
                  "commitment": "Committed", "confidence": 0.9 },
                { "key": "t2", "name": "Berth occupancy", "pricingModel": "RecurringAmount",
                  "unitRate": 1200, "currency": "USD", "billingRecurrence": "monthly",
                  "durationCount": 24, "durationUnit": "Month",
                  "commitment": "Committed", "confidence": 0.9 }
              ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Terms.Count);

        var money = result.Financials!;
        Assert.Equal(4_500m, money.CommittedOneTime);
        Assert.Equal(28_800m, money.CommittedRecurringTotal);
        Assert.Equal(33_300m, money.CommittedNet);
    }

    // ================================================================= arbitrary cycles

    [Theory]
    [InlineData("fortnightly", RecurrenceKind.Periodic, PeriodUnit.Week, 2)]
    [InlineData("every 3 weeks", RecurrenceKind.Periodic, PeriodUnit.Week, 3)]
    [InlineData("alle zwei Monate", RecurrenceKind.Periodic, PeriodUnit.Month, 2)]
    [InlineData("vierteljährlich", RecurrenceKind.Periodic, PeriodUnit.Quarter, 1)]
    [InlineData("per year", RecurrenceKind.Periodic, PeriodUnit.Year, 1)]
    [InlineData("semi-annual", RecurrenceKind.Periodic, PeriodUnit.Month, 6)]
    [InlineData("per usage", RecurrenceKind.PerUsage, null, 1)]
    [InlineData("on each milestone", RecurrenceKind.PerMilestone, null, 1)]
    [InlineData("einmalig", RecurrenceKind.None, null, 1)]
    public void Billing_cycles_are_read_by_meaning_across_languages(
        string phrase, RecurrenceKind kind, PeriodUnit? unit, int interval)
    {
        var recurrence = RecurrenceNormalizer.Normalize(phrase);

        Assert.Equal(kind, recurrence.Kind);
        Assert.Equal(unit, recurrence.Unit);
        Assert.Equal(interval, recurrence.Interval);

        // The wording is kept beside the meaning, so a normalisation can always
        // be checked against what the document actually said.
        Assert.Equal(phrase, recurrence.SourcePhrase);
    }

    [Fact]
    public void An_unrecognised_frequency_stays_unknown_rather_than_becoming_monthly()
    {
        var recurrence = RecurrenceNormalizer.Normalize("whenever the vessel calls at the port");

        Assert.Equal(RecurrenceKind.Unknown, recurrence.Kind);
        Assert.Null(recurrence.Unit);
        Assert.Equal("whenever the vessel calls at the port", recurrence.SourcePhrase);
    }

    // ================================================= billing vs delivery vs quantity

    [Fact]
    public async Task Delivery_frequency_billing_frequency_and_quantity_unit_stay_separate()
    {
        // Cleaning delivered three times a week, billed once a month, counted in
        // visits. Three different frequencies in one term, which the old single
        // BillingCycle field could not hold at all.
        const string response = """
            {
              "concepts": [],
              "terms": [ {
                "key": "t1", "name": "Site cleaning",
                "pricingModel": "RecurringAmount",
                "unitRate": 780, "currency": "EUR",
                "quantity": 13, "quantityUnit": "visits per month",
                "deliveryRecurrence": "every 3 days",
                "billingRecurrence": "monthly",
                "paymentSchedule": "30 days from invoice",
                "durationCount": 12, "durationUnit": "Month",
                "commitment": "Committed", "confidence": 0.85
              } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        var term = Assert.Single(result.Terms);

        Assert.Equal(PeriodUnit.Month, term.BillingRecurrence.Unit);
        Assert.Equal(PeriodUnit.Day, term.DeliveryRecurrence.Unit);
        Assert.Equal(3, term.DeliveryRecurrence.Interval);
        Assert.Equal("visits per month", term.QuantityUnit);
        Assert.Equal("30 days from invoice", term.PaymentSchedule);

        // Billing drives the money; delivery does not.
        Assert.Equal(121_680m, result.Financials!.CommittedRecurringTotal);   // 780 × 13 × 12
    }

    // ================================================================== pricing phases

    [Fact]
    public async Task Pricing_that_changes_over_time_becomes_phases_of_one_term()
    {
        // A franchise fee that steps up by trading year.
        const string response = """
            {
              "concepts": [],
              "terms": [ {
                "key": "t1", "name": "Franchise fee",
                "pricingModel": "RecurringAmount", "currency": "GBP",
                "commitment": "Committed", "confidence": 0.9,
                "phases": [
                  { "label": "Year one", "sequence": 1, "rate": 900,
                    "billingRecurrence": "monthly", "durationCount": 1, "durationUnit": "Year" },
                  { "label": "Year two onwards", "sequence": 2, "rate": 1400,
                    "billingRecurrence": "monthly", "durationCount": 2, "durationUnit": "Year" }
                ]
              } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        var term = Assert.Single(result.Terms);

        // One term, two phases — not two terms, which would double the count of
        // things the customer thinks they are buying.
        Assert.Equal(2, term.Phases.Count);
        Assert.Equal("Year one", term.Phases[0].Label);

        Assert.Equal(44_400m, result.Financials!.CommittedRecurringTotal);   // 12×900 + 24×1400
        Assert.Equal(1_400m, result.Financials.CommittedMonthlyEquivalent);
    }

    [Fact]
    public async Task A_phase_bounded_by_a_project_stage_keeps_the_stage_as_written()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ {
                "key": "t1", "name": "Trial monitoring",
                "pricingModel": "RecurringAmount", "currency": "EUR",
                "commitment": "Committed", "confidence": 0.8,
                "phases": [
                  { "label": "Recruitment", "sequence": 1, "rate": 5000,
                    "billingRecurrence": "monthly",
                    "endCondition": "until the last patient is enrolled" },
                  { "label": "Follow-up", "sequence": 2, "rate": 2000,
                    "billingRecurrence": "monthly",
                    "startCondition": "from last patient enrolled" }
                ]
              } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");
        var term = Assert.Single(result.Terms);

        // Phases are not months and not dates here. The condition survives
        // verbatim; nothing converts it into a date nobody agreed.
        Assert.Equal("until the last patient is enrolled", term.Phases[0].EndCondition);
        Assert.Equal("from last patient enrolled", term.Phases[1].StartCondition);
        Assert.Equal(0m, result.Financials!.CommittedRecurringTotal);
        Assert.NotEmpty(result.Financials.Unresolved);
    }

    // ============================================================ committed vs variable

    [Fact]
    public async Task A_rate_without_a_committed_quantity_does_not_reach_the_contract_value()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [
                { "key": "t1", "name": "Retainer", "pricingModel": "RecurringAmount",
                  "unitRate": 3000, "currency": "EUR", "billingRecurrence": "monthly",
                  "durationCount": 6, "durationUnit": "Month",
                  "commitment": "Committed", "confidence": 0.95 },
                { "key": "t2", "name": "Additional words", "pricingModel": "PerUnit",
                  "unitRate": 0.14, "currency": "EUR", "quantityUnit": "word",
                  "commitment": "Variable", "confidence": 0.9 }
              ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        Assert.Equal(18_000m, result.Financials!.CommittedNet);

        // The per-word rate is recorded in full and excluded from the value.
        var perWord = result.Terms.Single(t => t.Name == "Additional words");
        Assert.Equal(0.14m, perWord.UnitRate.Value);
        Assert.Equal(Commitment.Variable, perWord.Commitment);
        Assert.True(result.Financials.IsPartial);
    }

    [Fact]
    public async Task An_amount_whose_firmness_is_unstated_is_flagged_not_assumed()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ { "key": "t1", "name": "Handling", "pricingModel": "FixedAmount",
                           "fixedAmount": 2000, "currency": "EUR",
                           "billingRecurrence": "one-time", "confidence": 0.6 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        var term = Assert.Single(result.Terms);

        // Absent commitment is Unknown, never Committed. A number is not a promise.
        Assert.Equal(Commitment.Unknown, term.Commitment);
        Assert.Equal(0m, result.Financials!.CommittedNet);
        Assert.Contains(term.OpenQuestions, q => q.Contains("committed", StringComparison.OrdinalIgnoreCase));
    }

    // ========================================================== unknown and incomplete

    [Fact]
    public async Task Missing_information_stays_missing()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ { "key": "t1", "name": "Advisory support",
                           "pricingModel": "Unknown", "currency": null,
                           "unitRate": null, "fixedAmount": null,
                           "billingRecurrence": null, "confidence": 0.4 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");
        var term = Assert.Single(result.Terms);

        Assert.False(term.UnitRate.HasValue);
        Assert.Null(term.FixedAmount);
        Assert.Equal(RecurrenceKind.Unknown, term.BillingRecurrence.Kind);
        Assert.Equal(0m, result.Financials!.CommittedNet);
    }

    [Fact]
    public async Task A_quantity_of_unknown_size_is_not_defaulted_to_one()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ { "key": "t1", "name": "Sample analysis",
                           "pricingModel": "PerUnit", "unitRate": 62, "currency": "CHF",
                           "quantity": null, "quantityUnit": "sample",
                           "commitment": "Variable", "confidence": 0.9 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        Assert.Null(result.Terms[0].Quantity);
        Assert.Equal(0m, result.Financials!.CommittedNet);
        Assert.Contains(result.Financials.Unresolved,
            u => u.Reason.Contains("quantity", StringComparison.OrdinalIgnoreCase));
    }

    // ======================================================= unsupported pricing models

    [Fact]
    public async Task A_pricing_arrangement_outside_the_known_set_is_kept_verbatim()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ { "key": "t1", "name": "Carbon adjustment",
                           "pricingModel": "indexed quarterly to the EU ETS settlement price",
                           "currency": "EUR", "commitment": "Variable", "confidence": 0.7 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");
        var term = Assert.Single(result.Terms);

        // Not squeezed into Tiered or Percentage, which would state a basis the
        // agreement does not use.
        Assert.Equal(PricingModelKind.Custom, term.PricingModel);
        Assert.Equal("indexed quarterly to the EU ETS settlement price", term.CustomPricingModel);
        Assert.Contains(term.OpenQuestions, q => q.Contains("does not match a known basis"));
    }

    // ======================================================== not everything is a term

    [Fact]
    public async Task Concepts_that_are_not_charges_do_not_become_terms()
    {
        const string response = """
            {
              "concepts": [
                { "key": "c1", "kind": "LegalClause", "summary": "Governing law",
                  "sourceSnippet": "This agreement is governed by the laws of …", "confidence": 0.95 },
                { "key": "c2", "kind": "PaymentCondition", "summary": "Late payment interest at 8%",
                  "sourceSnippet": "Interest accrues at 8% …", "confidence": 0.9 },
                { "key": "c3", "kind": "Deadline", "summary": "Delivery within 20 working days",
                  "sourceSnippet": "…", "confidence": 0.9 },
                { "key": "c4", "kind": "BillablePosition", "summary": "Annual licence",
                  "sourceSnippet": "…", "confidence": 0.95 }
              ],
              "terms": [ { "key": "c4", "name": "Annual licence", "pricingModel": "FixedAmount",
                           "fixedAmount": 9000, "currency": "EUR", "billingRecurrence": "annually",
                           "durationCount": 3, "durationUnit": "Year",
                           "commitment": "Committed", "confidence": 0.95 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        // Four concepts recognised, one charge. An 8% late-payment rate is not
        // 8% of anything the customer owes today.
        Assert.Equal(4, result.Extraction!.Concepts.Count);
        Assert.Single(result.Terms);
        Assert.Equal(27_000m, result.Financials!.CommittedNet);
    }

    // ============================================================== partial validation

    [Fact]
    public async Task One_bad_field_does_not_discard_the_rest_of_the_extraction()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [
                { "key": "t1", "name": "Good term", "pricingModel": "FixedAmount",
                  "fixedAmount": 1000, "currency": "EUR", "billingRecurrence": "one-time",
                  "commitment": "Committed", "confidence": 0.9 },
                { "key": "t2", "name": "Bad dates", "pricingModel": "FixedAmount",
                  "fixedAmount": 500, "currency": "EUR", "billingRecurrence": "one-time",
                  "startDate": "2027-06-01", "endDate": "2027-01-01",
                  "commitment": "Committed", "confidence": 0.7 },
                { "key": "t3", "name": "Unreadable date", "pricingModel": "FixedAmount",
                  "fixedAmount": 250, "currency": "EUR", "billingRecurrence": "one-time",
                  "startDate": "sometime after the summer",
                  "commitment": "Committed", "confidence": 0.5 }
              ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        // All three survive. Re-typing the two that were right because the third
        // had a bad date is exactly what this avoids.
        Assert.Equal(3, result.Terms.Count);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Problem);

        var unreadable = result.Terms.Single(t => t.Name == "Unreadable date");
        Assert.Null(unreadable.StartDate);
        Assert.Contains(unreadable.OpenQuestions, q => q.Contains("could not be read as a date"));
    }

    [Fact]
    public async Task Contradictory_information_is_reported_rather_than_resolved()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [ { "key": "t1", "name": "Free trial with a price",
                           "pricingModel": "NoCharge", "fixedAmount": 400, "currency": "EUR",
                           "commitment": "Committed", "confidence": 0.5 } ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        // Neither half is deleted to make the other consistent; a person decides
        // which is wrong.
        Assert.Single(result.Terms);
        Assert.Contains(result.Issues, i =>
            i.Severity == ValidationSeverity.Problem &&
            i.Message.Contains("free of charge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicate_terms_are_flagged_and_both_are_kept()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [
                { "key": "t1", "name": "Inspection", "pricingModel": "FixedAmount",
                  "fixedAmount": 300, "currency": "EUR", "billingRecurrence": "one-time",
                  "commitment": "Committed", "confidence": 0.8 },
                { "key": "t2", "name": "Inspection", "pricingModel": "FixedAmount",
                  "fixedAmount": 300, "currency": "EUR", "billingRecurrence": "one-time",
                  "commitment": "Committed", "confidence": 0.8 }
              ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        // Whether a repeated charge is one thing or two is a judgement about the
        // agreement, and merging them silently changes the total either way.
        Assert.Equal(2, result.Terms.Count);
        Assert.Contains(result.Issues, i => i.Message.Contains("also called"));
    }

    [Fact]
    public async Task A_proposal_with_nothing_in_it_is_dropped_with_a_reason()
    {
        const string response = """
            {
              "concepts": [],
              "terms": [
                { "key": "t1", "name": null, "description": null, "confidence": 0.1 },
                { "key": "t2", "name": "Real term", "pricingModel": "FixedAmount",
                  "fixedAmount": 100, "currency": "EUR", "billingRecurrence": "one-time",
                  "commitment": "Committed", "confidence": 0.9 }
              ]
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        Assert.Single(result.Terms);
        Assert.Single(result.DiscardedReasons);
    }

    // ============================================================== the prompt itself

    [Fact]
    public async Task The_prompt_describes_a_domain_and_names_no_particular_business()
    {
        var stub = new StubAi("""{ "concepts": [], "terms": [] }""");

        await Analyzer(stub).AnalyzeAsync("any document at all");

        var prompt = stub.LastPrompt!;

        // The instructions that make the extraction generic.
        Assert.Contains("NEVER INVENT", prompt);
        Assert.Contains("ONLY CHARGES BECOME TERMS", prompt);
        Assert.Contains("NO ARITHMETIC", prompt);
        Assert.Contains("DISTINGUISH THREE DIFFERENT FREQUENCIES", prompt);
        Assert.Contains("PHASES, NOT SEPARATE TERMS", prompt);

        // And nothing tying it to one customer, service or industry. A prompt
        // written around one agreement reads the next as if it were that one.
        foreach (var leak in new[]
                 {
                     "Netwitcher", "E-Commerce", "TikTok", "SEO", "Agenturvertrag",
                     "harbring", "Kosmetikschule", "2500", "Shopify"
                 })
        {
            Assert.DoesNotContain(leak, prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_document_is_the_only_thing_that_varies_between_two_analyses()
    {
        var first = new StubAi("""{ "concepts": [], "terms": [] }""");
        var second = new StubAi("""{ "concepts": [], "terms": [] }""");

        await Analyzer(first).AnalyzeAsync("DOCUMENT ONE");
        await Analyzer(second).AnalyzeAsync("DOCUMENT TWO");

        var a = first.LastPrompt!.Replace("DOCUMENT ONE", "«doc»");
        var b = second.LastPrompt!.Replace("DOCUMENT TWO", "«doc»");

        // Identical instructions either way: no branch anywhere adapts the
        // reading to what the document appears to be about.
        Assert.Equal(a, b);
    }

    // ================================================================== currency

    [Theory]
    [InlineData("Euro", "EUR")]
    [InlineData("€", "EUR")]
    [InlineData("usd", "USD")]
    [InlineData("CHF", "CHF")]
    [InlineData("SEK", "SEK")]        // never seen before, still a valid code
    [InlineData("nach Absprache", null)]
    public void Currency_is_normalised_to_a_code_or_left_unset(string input, string? expected)
    {
        Assert.Equal(expected, ProposedTermMapper.NormaliseCurrency(input));
    }
}

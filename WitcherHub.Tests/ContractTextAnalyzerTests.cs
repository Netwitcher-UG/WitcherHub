using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests;

/// <summary>
/// Reading a supplied contract, and the rules about what the reader is not
/// allowed to do. No test here reaches the real OpenAI API.
///
/// The rule under test throughout: the analyser reports what the document says
/// and never fills a gap. A price nobody agreed is worse than no price.
/// </summary>
public class ContractTextAnalyzerTests
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

    private static ContractTextAnalyzer Analyzer(IAiTextGenerator ai) =>
        new(ai,
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            NullLogger<ContractTextAnalyzer>.Instance);

    private const string ItemisedResponse = """
        {
          "title": { "value": "Agenturvertrag", "sourceText": "AGENTURVERTRAG", "confidence": 0.95 },
          "totalPrice": { "value": "3600.00", "sourceText": "Gesamt 3.600,00 EUR", "confidence": 0.9 },
          "currency": { "value": "EUR", "sourceText": "EUR", "confidence": 0.99 },
          "vatRate": { "value": "19", "sourceText": "zzgl. 19% USt", "confidence": 0.9 },
          "positions": [
            { "title": "SEO Betreuung", "quantity": 12, "unit": "Monat", "unitPrice": 200,
              "lineTotal": 2400, "currency": "EUR", "vatRatePercent": 19,
              "billingCycle": "Monthly", "sourceText": "SEO 12 x 200 EUR", "confidence": 0.9 },
            { "title": "Website Relaunch", "quantity": 1, "unit": "Pauschale", "unitPrice": 1200,
              "lineTotal": 1200, "currency": "EUR", "vatRatePercent": 19,
              "billingCycle": "OneTime", "sourceText": "Relaunch 1.200 EUR", "confidence": 0.85 }
          ],
          "warnings": []
        }
        """;

    [Fact]
    public async Task A_contract_with_itemised_prices_is_read_for_review()
    {
        var result = await Analyzer(new StubAi(ItemisedResponse)).AnalyzeAsync("AGENTURVERTRAG …");

        Assert.True(result.Succeeded);

        var e = result.Extraction!;
        Assert.Equal(2, e.Positions.Count);
        Assert.Equal(200m, e.Positions[0].UnitPrice);
        Assert.Equal("3600.00", e.TotalPrice.Value);

        // Read for review, not applied: nothing arrives ticked.
        Assert.All(e.Positions, p => Assert.False(p.Accepted));
        Assert.All(new[] { e.TotalPrice, e.Currency, e.VatRate },
            v => Assert.True(v.NeedsConfirmation && !v.Confirmed));
    }

    [Fact]
    public async Task One_total_with_no_itemised_services_produces_no_invented_positions()
    {
        const string response = """
            {
              "title": { "value": "Rahmenvertrag", "sourceText": "RAHMENVERTRAG", "confidence": 0.9 },
              "totalPrice": { "value": "12000", "sourceText": "Pauschal 12.000 EUR", "confidence": 0.95 },
              "currency": { "value": "EUR", "sourceText": "EUR", "confidence": 0.9 },
              "positions": [],
              "warnings": []
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("RAHMENVERTRAG …");

        Assert.True(result.Succeeded);

        // A single agreed sum is one fact, not a list of services. Splitting it
        // into line items would be inventing an itemisation nobody agreed.
        Assert.Empty(result.Extraction!.Positions);
        Assert.Equal("12000", result.Extraction.TotalPrice.Value);
        Assert.False(result.Extraction.PriceMissing);
    }

    [Fact]
    public async Task A_contract_with_no_price_gets_no_price_and_a_warning()
    {
        const string response = """
            {
              "title": { "value": "Kooperationsvertrag", "sourceText": "KOOPERATION", "confidence": 0.8 },
              "totalPrice": { "value": null, "sourceText": null, "confidence": 0 },
              "positions": [],
              "warnings": []
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("KOOPERATIONSVERTRAG …");

        Assert.True(result.Succeeded);

        var e = result.Extraction!;
        Assert.True(e.PriceMissing);
        Assert.False(e.TotalPrice.HasValue);
        Assert.Contains(e.Warnings, w => w.Contains("price", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_total_that_disagrees_with_the_line_items_is_reported_not_reconciled()
    {
        const string response = """
            {
              "totalPrice": { "value": "5000", "sourceText": "Gesamt 5.000 EUR", "confidence": 0.9 },
              "positions": [
                { "title": "A", "quantity": 1, "unitPrice": 1000, "lineTotal": 1000, "confidence": 0.9 },
                { "title": "B", "quantity": 1, "unitPrice": 1000, "lineTotal": 1000, "confidence": 0.9 }
              ],
              "warnings": []
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        var e = result.Extraction!;

        // Neither figure is changed to fit the other; the disagreement is the
        // finding.
        Assert.Equal("5000", e.TotalPrice.Value);
        Assert.Equal(2000m, e.Positions.Sum(p => p.LineTotal));
        Assert.Contains(e.Warnings, w => w.Contains("does not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Confidence_claimed_outside_the_scale_is_brought_back_inside_it()
    {
        const string response = """
            { "title": { "value": "X", "sourceText": "X", "confidence": 7.5 }, "positions": [], "warnings": [] }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        Assert.Equal(1d, result.Extraction!.Title.Confidence);
    }

    [Fact]
    public async Task A_response_that_claims_a_value_is_already_confirmed_is_not_believed()
    {
        const string response = """
            {
              "totalPrice": { "value": "999", "sourceText": "999", "confidence": 1,
                              "needsConfirmation": false, "confirmed": true },
              "positions": [ { "title": "A", "unitPrice": 999, "confidence": 1, "accepted": true } ],
              "warnings": []
            }
            """;

        var result = await Analyzer(new StubAi(response)).AnalyzeAsync("…");

        // Confirmation is an act by a person. A model asserting it does not make
        // it so, and a commercial value stays flagged however sure it claims to be.
        Assert.False(result.Extraction!.TotalPrice.Confirmed);
        Assert.True(result.Extraction.TotalPrice.NeedsConfirmation);
        Assert.False(result.Extraction.Positions[0].Accepted);
    }

    [Fact]
    public async Task An_unreachable_assistant_names_the_actual_problem()
    {
        var failing = new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.ModelUnavailable, "404", "ABCD1234", 404));

        var result = await Analyzer(failing).AnalyzeAsync("…");

        Assert.False(result.Succeeded);

        // "Not reachable right now" was the same sentence for a missing key, an
        // unusable model and a rate limit. It has to say which.
        Assert.Contains("model", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ABCD1234", result.FailureReason!);
        Assert.Equal("ABCD1234", result.CorrelationId);

        // Not worth retrying: a model the account cannot use will not fix itself.
        Assert.False(result.IsTransientFailure);
    }

    [Fact]
    public async Task A_rate_limit_is_offered_as_worth_retrying()
    {
        var failing = new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.RateLimited, "429", "FFFF0000", 429));

        var result = await Analyzer(failing).AnalyzeAsync("…");

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task An_unreadable_answer_leaves_the_document_alone()
    {
        var result = await Analyzer(new StubAi("I'm afraid I can't do that.")).AnalyzeAsync("…");

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
        Assert.Contains("saved and unchanged", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void German_and_invariant_amounts_are_both_read()
    {
        Assert.True(ContractTextAnalyzer.TryParseAmount("1.234,56 EUR", out var german));
        Assert.Equal(1234.56m, german);

        Assert.True(ContractTextAnalyzer.TryParseAmount("EUR 1,234.56", out var invariant));
        Assert.Equal(1234.56m, invariant);

        Assert.True(ContractTextAnalyzer.TryParseAmount("3600.00", out var plain));
        Assert.Equal(3600.00m, plain);

        Assert.False(ContractTextAnalyzer.TryParseAmount("nach Vereinbarung", out _));
    }

    [Fact]
    public async Task The_prompt_forbids_inventing_a_missing_value()
    {
        var stub = new StubAi("""{ "positions": [], "warnings": [] }""");

        await Analyzer(stub).AnalyzeAsync("Vertrag …");

        Assert.Contains("null", stub.LastPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never supply a plausible value", stub.LastPrompt!, StringComparison.Ordinal);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts.Language;
using WitcherHub.Infrastructure.Services.Contracts.Language;

namespace WitcherHub.Tests;

/// <summary>
/// The signing page shows the contract in the language the customer picked.
///
/// The EN/DE buttons set the culture cookie and reloaded, which translated the
/// page's own labels through the resource files and left the contract itself in
/// German — an English shell around a German document, which is arguably worse
/// than not offering the choice, because it looks as though it worked.
///
/// The contract is translated now. Carefully: the German remains the agreement
/// and the only thing signed, and the structure of the document never reaches
/// the model. These are about what comes back and what is refused.
///
/// The model is stubbed throughout. No key, no call.
/// </summary>
public class TheCustomerReadsTheContractInTheirLanguageTests
{
    private const string Contract = """
        # Agenturvertrag

        **Vertragsnummer:** C-4711

        ## 1. Leistungsbeschreibung

        Laufende Betreuung der Vertriebskanäle.

        ## 2. Vergütung

        | Pos. | Bezeichnung | Netto |
        |---|---|---:|
        | 1 | Monatliche Betreuung | 1.900,00 EUR |
        | | Umsatzsteuer | 361,00 EUR |

        (1) Rechnungen sind innerhalb von 14 Tagen fällig.
        """;

    /// <summary>Answers with the German upper-cased, which is enough to see structure survive.</summary>
    private sealed class Shouts : IAiTextGenerator
    {
        public string? LastPrompt { get; private set; }
        public string? LastSystem { get; private set; }
        public int Calls { get; private set; }

        public Task<string> GenerateTextAsync(string prompt) =>
            CompleteAsync(new AiRequest(prompt)).ContinueWith(t => t.Result.Text);

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastPrompt = request.Prompt;
            LastSystem = request.SystemInstruction;

            var answer = new
            {
                segments = Ids(request.Prompt)
                    .Select(x => new { id = x.Id, text = x.Text.ToUpperInvariant() }),
                reviewFlags = Array.Empty<object>()
            };

            return Task.FromResult(new AiCompletion(JsonSerializer.Serialize(answer), AiFinishReason.Stop));
        }
    }

    /// <summary>Answers with something that is not the agreed shape.</summary>
    private sealed class Answers(string raw) : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) => Task.FromResult(raw);

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AiCompletion(raw, AiFinishReason.Stop));
    }

    private sealed class Throws : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) => throw Failure();

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default) =>
            throw Failure();

        private static AiInvocationException Failure() =>
            new(AiFailureKind.Timeout, "took too long", "REF-VIEW");
    }

    private static IEnumerable<(string Id, string Text)> Ids(string prompt)
    {
        var segments = prompt[..prompt.IndexOf("PROTECTED VALUES", StringComparison.Ordinal)];

        return Regex.Matches(segments, @"""id"":\s*""([^""]+)"",\s*""text"":\s*""((?:[^""\\]|\\.)*)""")
            .Select(m => (m.Groups[1].Value, Regex.Unescape(m.Groups[2].Value)));
    }

    private static OpenAiContractViewTranslator Translator(IAiTextGenerator ai) =>
        new(ai, NullLogger<OpenAiContractViewTranslator>.Instance);

    // ================================================================ it works

    [Fact]
    public async Task TheProseIsTranslatedAndTheFiguresAreNot()
    {
        var result = await Translator(new Shouts()).TranslateAsync(Contract, "en", []);

        Assert.True(result.Succeeded, result.FailureReason);

        var markdown = result.Markdown!;

        // The words moved …
        Assert.Contains("LAUFENDE BETREUUNG DER VERTRIEBSKANÄLE.", markdown);
        Assert.Contains("## 1. LEISTUNGSBESCHREIBUNG", markdown);

        // … and nothing else did.
        Assert.Contains("| 1 | MONATLICHE BETREUUNG | 1.900,00 EUR |", markdown);
        Assert.Contains("361,00 EUR", markdown);
        Assert.Contains("(1) RECHNUNGEN SIND INNERHALB VON 14 TAGEN FÄLLIG.", markdown);
        Assert.Contains("|---|---|---:|", markdown);
    }

    [Fact]
    public async Task TheContractNumberIsNeverSentAndNeverChanges()
    {
        var ai = new Shouts();

        var result = await Translator(ai).TranslateAsync(Contract, "en", []);

        // The label travels; the number beside it does not.
        Assert.DoesNotContain("C-4711", SegmentsBlock(ai.LastPrompt!));
        Assert.Contains("**VERTRAGSNUMMER:** C-4711", result.Markdown!);
    }

    [Fact]
    public async Task PartyNamesAreMaskedBeforeTheyReachTheModel()
    {
        var ai = new Shouts();

        const string withParty = "Der Anbieter Netwitcher UG erbringt die Leistungen.";

        var result = await Translator(ai).TranslateAsync(
            withParty, "en",
            [new ProtectedValue("", "Netwitcher UG", ProtectedValueKind.LegalCompanyName)]);

        Assert.True(result.Succeeded, result.FailureReason);

        // Never seen …
        Assert.DoesNotContain("Netwitcher UG", SegmentsBlock(ai.LastPrompt!));
        Assert.Contains("{{PROTECTED_001}}", SegmentsBlock(ai.LastPrompt!));

        // … and back exactly as it was, not upper-cased with the rest.
        Assert.Contains("Netwitcher UG", result.Markdown!);
        Assert.DoesNotContain("PROTECTED", result.Markdown!);
    }

    [Fact]
    public async Task TheModelIsToldTheGermanIsBinding()
    {
        var ai = new Shouts();

        await Translator(ai).TranslateAsync(Contract, "en", []);

        Assert.Contains("The German text is the agreement", ai.LastSystem!);
        Assert.Contains("reading aid", ai.LastSystem!);
        Assert.Contains("into English", ai.LastSystem!);

        // And the schema it has to answer in.
        Assert.Contains("\"required\": [\"segments\",\"reviewFlags\"]", ai.LastSystem!);
    }

    [Fact]
    public async Task AGermanViewNeedsNoTranslationAtAll()
    {
        var ai = new Shouts();

        // "de" is a supported language, but the page never asks for it — the
        // contract is already German. Asking anyway must still be safe.
        var result = await Translator(ai).TranslateAsync(Contract, "de", []);

        Assert.True(result.Succeeded, result.FailureReason);
    }

    [Fact]
    public async Task AnUnsupportedLanguageIsRefusedRatherThanAttempted()
    {
        var result = await Translator(new Shouts()).TranslateAsync(Contract, "fr", []);

        Assert.False(result.Succeeded);
        Assert.Contains("fr", result.FailureReason!, StringComparison.Ordinal);
    }

    // ============================================== answers that are refused

    [Fact]
    public async Task AnIncompleteAnswerIsRefused()
    {
        // One segment back, many asked for. A document assembled from this is
        // one whose untranslated half the reader cannot identify.
        var result = await Translator(new Answers(
                """{"segments":[{"id":"L0","text":"Agency contract"}],"reviewFlags":[]}"""))
            .TranslateAsync(Contract, "en", []);

        Assert.False(result.Succeeded);
        Assert.Contains("unvollständig", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidJsonIsRefused()
    {
        var result = await Translator(new Answers("not json")).TranslateAsync(Contract, "en", []);

        Assert.False(result.Succeeded);
        Assert.Null(result.Markdown);
    }

    [Fact]
    public async Task ADuplicatedSegmentIsRefused()
    {
        var result = await Translator(new Answers(
                """{"segments":[{"id":"L0","text":"One"},{"id":"L0","text":"Two"}],"reviewFlags":[]}"""))
            .TranslateAsync(Contract, "en", []);

        Assert.False(result.Succeeded);
        Assert.Contains("doppelte", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedCallShowsNoTranslationRatherThanHalfOne()
    {
        var result = await Translator(new Throws()).TranslateAsync(Contract, "en", []);

        Assert.False(result.Succeeded);
        Assert.Null(result.Markdown);

        // The reference goes to the log; the reader gets a sentence.
        Assert.DoesNotContain("REF-VIEW", result.FailureReason!);
    }

    [Fact]
    public async Task AnAnswerThatMovesAFigureIsRefused()
    {
        // The one thing a reading aid must never do. The stub returns a segment
        // with a deadline the German did not have.
        var ai = new InventsANumber();

        var result = await Translator(ai).TranslateAsync(
            "Rechnungen sind nach Zugang fällig.", "en", []);

        Assert.False(result.Succeeded);
        Assert.Contains("weicht inhaltlich ab", result.FailureReason!.Replace(" vom Vertragstext", ""));
    }

    [Fact]
    public async Task AClauseTooLongForTheSchemaIsRefusedBeforeItIsSent()
    {
        var ai = new Shouts();

        // The schema caps what can come back. A paragraph over the limit would
        // be rejected by the schema rather than answered, which reaches the
        // reader as a translation that never works and no reason why — so it is
        // refused here, and the model is not called at all.
        var result = await Translator(ai).TranslateAsync(
            "Die Parteien vereinbaren " + new string('x', 4100), "en", []);

        Assert.False(result.Succeeded);
        Assert.Equal(0, ai.Calls);
        Assert.Contains("zu lang", result.FailureReason!, StringComparison.Ordinal);
    }

    private sealed class InventsANumber : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) =>
            CompleteAsync(new AiRequest(prompt)).ContinueWith(t => t.Result.Text);

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            var answer = new
            {
                segments = Ids(request.Prompt)
                    .Select(x => new { id = x.Id, text = "Invoices are due within 14 days." }),
                reviewFlags = Array.Empty<object>()
            };

            return Task.FromResult(new AiCompletion(JsonSerializer.Serialize(answer), AiFinishReason.Stop));
        }
    }

    // ================================================================= batching

    [Fact]
    public void LongContractsAreSplitAtSegmentBoundariesOnly()
    {
        var segments = Enumerable.Range(0, 150)
            .Select(i => new MarkdownSegment(i, -1, "", "", new string('x', 400)))
            .ToList();

        var batches = OpenAiContractViewTranslator.Batch(segments).ToList();

        Assert.True(batches.Count > 1);

        // Every segment once, in order. Splitting inside one would leave half a
        // sentence to be translated without the other half.
        Assert.Equal(
            segments.Select(s => s.FieldId),
            batches.SelectMany(b => b).Select(s => s.FieldId));
    }

    // =================================================================== helper

    private static string SegmentsBlock(string prompt) =>
        prompt[..prompt.IndexOf("PROTECTED VALUES", StringComparison.Ordinal)];
}

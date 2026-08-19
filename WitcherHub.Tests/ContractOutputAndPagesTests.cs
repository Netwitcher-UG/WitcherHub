using OpenAI.Chat;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using WitcherHub.Infrastructure.Services.Pdf;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Nothing between the model and the page may shorten a contract.
    ///
    /// The document was short for one reason above all — the prompt asked for
    /// seven headings — but the diagnosis had to rule out the others before that
    /// was worth acting on, and two of them were real:
    ///
    ///   * no output budget was ever set and the finish reason was never read, so
    ///     an answer the provider cut off was saved as a finished contract;
    ///   * only the first content part of a reply was kept.
    ///
    /// Two were not: the column is unbounded <c>text</c>, and the PDF pipeline
    /// already paginated properly. The tests for those are here too, because "we
    /// checked" is worth keeping and the next person should not have to check
    /// again.
    /// </summary>
    public class ContractOutputAndPagesTests
    {
        // ================================================ the transport layer

        [Fact]
        public void AnAnswerThatRanOutOfRoomSaysSo()
        {
            Assert.True(new AiCompletion("half a sen", AiFinishReason.Length).IsTruncated);
            Assert.False(new AiCompletion("all of it", AiFinishReason.Stop).IsTruncated);
        }

        [Theory]
        [InlineData("Stop", AiFinishReason.Stop)]
        [InlineData("Length", AiFinishReason.Length)]
        [InlineData("ContentFilter", AiFinishReason.ContentFilter)]
        [InlineData("ToolCalls", AiFinishReason.Other)]
        public void TheProvidersStopReasonIsTranslatedRatherThanIgnored(string reason, AiFinishReason expected)
        {
            var parsed = Enum.Parse<ChatFinishReason>(reason);

            Assert.Equal(expected, OpenAiTextGenerator.Translate(parsed));
        }

        [Fact]
        public async Task AStubThatOnlyAnswersPromptsStillReportsAFinishedAnswer()
        {
            // Nine test doubles implement the one-string method. The richer call
            // has a default so they keep working, and what it reports has to be
            // "finished" — a stub returns a whole answer.
            IAiTextGenerator simple = new OneStringAi("done");

            var answer = await simple.CompleteAsync(new AiRequest("anything"));

            Assert.Equal("done", answer.Text);
            Assert.False(answer.IsTruncated);
        }

        private sealed class OneStringAi(string answer) : IAiTextGenerator
        {
            public Task<string> GenerateTextAsync(string prompt) => Task.FromResult(answer);
        }

        [Theory]
        [InlineData(0, 256)]
        [InlineData(-1, 256)]
        [InlineData(1, 256)]
        [InlineData(16000, 16000)]
        [InlineData(999_999, 128_000)]
        public void TheOutputBudgetIsClampedToSomethingUsable(int configured, int expected)
        {
            // A budget below the length of one clause turns every generation into a
            // truncation, which reads as a broken assistant rather than as a
            // mistyped setting.
            var options = new OpenAIOptions { MaxOutputTokens = configured };

            Assert.Equal(expected, options.EffectiveMaxOutputTokens);
        }

        [Fact]
        public void TheDefaultBudgetIsRoomForALongContract()
        {
            Assert.True(new OpenAIOptions().EffectiveMaxOutputTokens >= 8000);
        }

        [Fact]
        public void TheOneCallToTheModelSendsTheRulesAndReadsTheStopReason()
        {
            var source = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "OpenAI",
                "OpenAiTextGenerator.cs"));

            // Was: CompleteChatAsync(prompt) — one user message, no options at all,
            // so OpenAI__TimeoutSeconds had a sibling setting that did not exist
            // and the answer's length was decided by the provider's default.
            Assert.Contains("SystemChatMessage", source);
            Assert.Contains("ChatCompletionOptions", source);
            Assert.Contains("MaxOutputTokenCount", source);

            // And the finish reason is read. Without this a cut-off answer and a
            // complete one are the same string with the same properties.
            Assert.Contains("completion.FinishReason", source);

            // Every content part, not the first. A multi-part reply lost everything
            // after part one before it reached the parser.
            Assert.Contains("completion.Content.Select", source);
            Assert.DoesNotContain("completion.Content[0].Text", source);
        }

        [Fact]
        public void NothingLogsThePromptTheAnswerOrTheKey()
        {
            var source = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "OpenAI",
                "OpenAiTextGenerator.cs"));

            // The new telemetry is counts and a purpose label. Contract text and
            // customer data must not reach the platform log.
            Assert.DoesNotContain("{Prompt}", source);
            Assert.DoesNotContain("{Answer}", source);
            Assert.DoesNotContain("_options.ApiKey", source);

            // And provider messages quote the request back, so anything key-shaped
            // in them is removed.
            Assert.Contains("Redact(", source);
        }

        // ================================================== not the database

        [Fact]
        public void TheStoredDocumentHasNoLengthLimit()
        {
            var entity = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Data", "Models", "Contract.cs"));

            var at = entity.IndexOf("public string DocumentMarkdown", StringComparison.Ordinal);
            Assert.True(at > 0, "DocumentMarkdown is no longer where this test looks for it");

            // The 400 characters before it. A [MaxLength] here would silently cut
            // long contracts at save time, which is the failure this rules out.
            var before = entity[Math.Max(0, at - 400)..at];

            Assert.DoesNotContain("MaxLength", before);
        }

        // ============================================== the prompt's own shape

        [Fact]
        public void TheModelIsNoLongerToldHowManySectionsToWrite()
        {
            var prompt = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Application", "Services", "Contracts",
                "ContractGeneratorPrompt.cs"));

            // The list that made every contract one page:
            //   "Produce these sections, in this order …
            //    1. Gegenstand des Vertrags … 7. Laufzeit und Aktivierung"
            Assert.DoesNotContain("Produce these sections, in this order", prompt);
            Assert.DoesNotContain("7. Laufzeit und Aktivierung", prompt);

            // What replaced it: the plan is derived from the content, and length is
            // explicitly not a target.
            Assert.Contains("There is no minimum and no maximum number of sections", prompt);
            Assert.Contains("Do not aim for a page count", prompt);
        }

        [Fact]
        public void ThePlanMustAccountForEverythingExactlyOnce()
        {
            var prompt = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Application", "Services", "Contracts",
                "ContractGeneratorPrompt.cs"));

            Assert.Contains("must appear in exactly one", prompt);
            Assert.Contains("Nothing may be left unassigned", prompt);
        }

        [Fact]
        public void TheInternalIdsAreExplicitlyNotForTheCustomer()
        {
            var prompt = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Application", "Services", "Contracts",
                "ContractGeneratorPrompt.cs"));

            Assert.Contains("Never write an id into contract text", prompt);
        }

        [Fact]
        public void TheRulesThatKeepTheModelHonestSurvivedTheRewrite()
        {
            var prompt = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Application", "Services", "Contracts",
                "ContractGeneratorPrompt.cs"));

            // Carried over from v2 rather than lost in it. Each of these is a thing
            // the model must not do to somebody's contract.
            Assert.Contains("der Auftragnehmer", prompt);
            Assert.Contains("Never write either company name into", prompt);
            Assert.Contains("Do not put the party", prompt);
            Assert.Contains("Do not write liability", prompt);
            Assert.Contains("wird noch festgelegt", prompt);
        }

        [Fact]
        public void APastedDocumentIsReadWholeRatherThanInAQuarter()
        {
            // v2 gave the pasted text 24.000 characters of a request it shared with
            // the whole contract's data. Reading it is now its own call, so the
            // document can have the room.
            Assert.True(ContractGeneratorPrompt.SourceTextBudget >= 100_000);
        }

        [Fact]
        public void AVeryLongDocumentIsCutVisiblyRatherThanSilently()
        {
            var cut = ContractGeneratorPrompt.Truncate(new string('x', 200), 100);

            Assert.Contains("gekürzt", cut);
        }

        // ==================================================== the printed page

        private static string Css() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "wwwroot", "css", "contracts", "contract-sign.css"));

        [Fact]
        public void TheSheetIsNeverScaledToFit()
        {
            var css = Css();

            // The two ways a long document gets made to look short. Neither is
            // acceptable: a contract that needs eight pages gets eight pages.
            Assert.DoesNotContain("transform: scale(", css);
            Assert.DoesNotContain("zoom:", css);
        }

        [Fact]
        public void TheSheetIsNeverClipped()
        {
            var css = Css();

            var at = css.IndexOf(".contractPage__paper {", StringComparison.Ordinal);
            Assert.True(at > 0);

            // Every rule that targets the paper itself. `overflow: hidden` here
            // would cut the document off at whatever height the box happened to
            // have, which is indistinguishable from a short contract.
            while (at > 0)
            {
                var end = css.IndexOf('}', at);

                // Declarations only. The comment inside that block explains why
                // `overflow: hidden` is absent, and asserting against the prose
                // rather than the rules is how a test comes to fail on its own
                // explanation.
                var block = WithoutComments(css[at..end]);

                Assert.DoesNotContain("overflow: hidden", block);
                Assert.DoesNotContain("max-height", block);

                at = css.IndexOf(".contractPage__paper {", end, StringComparison.Ordinal);
            }
        }

        private static string WithoutComments(string css) =>
            System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        [Fact]
        public void TheSheetIsA4Wide()
        {
            var css = Css();

            Assert.Contains("width: 210mm", css);
            Assert.Contains("size: A4", css);
        }

        [Fact]
        public void ThePageMarksAreDrawnOverTheDocumentNotInIt()
        {
            var css = Css();

            // Absolutely positioned and non-interactive, so counting the pages
            // cannot reflow the thing being counted.
            var at = css.IndexOf(".contractPaper__pages {", StringComparison.Ordinal);
            Assert.True(at > 0, "the page overlay has no styles");

            var block = css[at..css.IndexOf('}', at)];

            Assert.Contains("position: absolute", block);
            Assert.Contains("pointer-events: none", block);
        }

        [Fact]
        public void PrintingDropsTheScreensPageMarksAndItsPaddedHeight()
        {
            var print = Css()[Css().LastIndexOf("@media print", StringComparison.Ordinal)..];

            // The preview pads the sheet to whole pages so the last one looks
            // finished. On paper that is blank pages at the end.
            Assert.Contains("min-height: 0 !important", print);

            // And a dashed rule with "Seite 3 von 8" printed across a clause is
            // exactly the sort of thing that ends up in front of a customer.
            Assert.Contains(".contractPaper__pages", print);
            Assert.Contains("display: none !important", print);
        }

        [Fact]
        public void CountingThePagesChangesNothingAboutThem()
        {
            var script = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "contract-pages.js"));

            // The one thing this script must never do. It measures and draws; it
            // does not make the contract fit.
            Assert.DoesNotContain("scale(", script);
            Assert.DoesNotContain("fontSize", script);
            Assert.DoesNotContain("overflow", script);

            // It reports how many pages there are, which is the point.
            Assert.Contains("Seite ", script);
            Assert.Contains("pageCount", script);
        }

        // ========================================================== the logo

        [Fact]
        public void TheConfiguredLogoIsAFileThatExists()
        {
            // It was not. The PDF generator looked under wwwroot/theme/assets/images,
            // which this repository does not have, logged a warning nobody read, and
            // substituted an empty src — so every server-generated PDF went out
            // unbranded while the same contract on screen carried the logo.
            var configured = new BrandingOptions().LogoPath;

            var path = Path.Combine(
                new[] { TestPaths.WebProject, "wwwroot" }.Concat(configured.Split('/')).ToArray());

            Assert.True(File.Exists(path), $"no logo at the configured default path: {configured}");
        }

        [Fact]
        public void TheLogoPathIsASettingRatherThanALiteral()
        {
            var generator = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "Pdf",
                "PlaywrightPdfGenerator.cs"));

            Assert.DoesNotContain("\"theme\", \"assets\", \"images\"", generator);
            Assert.Contains("_branding.LogoPath", generator);

            // And a missing file names the setting an owner has to change.
            Assert.Contains("LogoPathSettingName", generator);
        }

        [Fact]
        public void TheContractSheetCarriesTheLogoAndTheContractNumber()
        {
            var letterhead = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Shared", "_ContractLetterhead.cshtml"));

            Assert.Contains("contractPaper__logo", letterhead);
            Assert.Contains("Model.ContractNo", letterhead);

            // With no logo configured the company's name stands in, rather than a
            // blank corner that reads as a broken image.
            Assert.Contains("contractPaper__wordmark", letterhead);

            var page = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Contracts", "Details.cshtml"));

            Assert.Contains("_ContractLetterhead", page);
            Assert.Contains("contract-pages.js", page);
        }

        // ============================================ what the user is told

        [Fact]
        public void WhatTheContractDoesNotCoverReachesTheScreen()
        {
            var page = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Contracts", "Positions.cshtml.cs"));

            // Generation used to answer with "version 3 created as a draft" and
            // nothing else, whether it had covered everything or a third of it.
            Assert.Contains("reviewNotes = result.ReviewNotes", page);

            var script = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "positions-builder.js"));

            // The page reloads straight after generating, so a toast raised at the
            // moment of success announces itself to a page that is already being
            // replaced. The notes are put aside and read on the way back.
            Assert.Contains("REVIEW_NOTES_KEY", script);
            Assert.Contains("showPendingReviewNotes()", script);

            // And they stay up: this is read before somebody approves a contract.
            Assert.Contains("sticky: true", script);
        }

        [Fact]
        public void TheContractNumberInTheDocumentComesFromTheRecord()
        {
            var document = GermanContractDocument.Compose(
                title: "Dienstleistungsvertrag",
                contractNo: "C-2030-000042",
                projectTitle: "Projekt",
                parties: new GermanContractDocument.Parties("A", null, "B", null),
                clauses: "## § 1 Test");

            // Never composed, never generated, never inferred from a version number.
            Assert.Contains("Vertragsnummer: C-2030-000042", document);
        }
    }
}

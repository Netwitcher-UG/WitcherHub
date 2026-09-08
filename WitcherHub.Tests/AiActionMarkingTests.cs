using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Every button that sends work to the language model is marked, and nothing
    /// else wears the mark.
    ///
    /// Asked for directly: the owner wanted to know before pressing which buttons
    /// return an answer from the model. That distinction is worth showing —
    /// an AI action spends money on the OpenAI account, takes seconds rather than
    /// milliseconds, fails for reasons nothing else fails for, and returns a
    /// proposal a person still has to check.
    ///
    /// A signal like this only works while it is complete. One unmarked AI button
    /// teaches the reader the mark cannot be trusted, and one marked ordinary
    /// button does the same, so both directions are checked here rather than left
    /// to whoever adds the next action.
    /// </summary>
    public class AiActionMarkingTests
    {
        /// <summary>
        /// The actions whose handlers reach <c>IAiTextGenerator</c>:
        /// Analyze (twice over, from two buttons), Organize and GenerateDraft.
        /// </summary>
        private static readonly string[] AiActions =
        [
            "analyze-source",
            "extract-positions",
            "organize",
            "run-organize",
            "generate-draft"
        ];

        /// <summary>
        /// Actions on the same page that apply an answer already obtained, or do
        /// something entirely local. Pressing these costs nothing and calls
        /// nobody, so marking them would make the mark meaningless.
        /// </summary>
        private static readonly string[] OrdinaryActions =
        [
            "add-manual",
            "add-catalog",
            "save",
            "import-text",
            "toggle-paste",
            "apply-organize",
            "confirm-extraction",
            "add-extracted-positions",
            "approve-version"
        ];

        [Fact]
        public void EveryAiActionCarriesTheMark()
        {
            var page = PositionsPage();

            foreach (var action in AiActions)
            {
                var button = ButtonFor(page, action);

                Assert.True(
                    button.Contains("_AiMark", StringComparison.Ordinal),
                    $"The '{action}' button calls the model but does not render the AI mark.");

                Assert.True(
                    button.Contains("wh-ai-action", StringComparison.Ordinal),
                    $"The '{action}' button is missing the wh-ai-action class that styles the mark.");

                Assert.True(
                    button.Contains("data-ai=\"true\"", StringComparison.Ordinal),
                    $"The '{action}' button is not machine-identifiable as an AI action.");
            }
        }

        [Fact]
        public void EveryAiActionSaysWhatItWillDoBeforeItIsPressed()
        {
            var page = PositionsPage();

            foreach (var action in AiActions)
            {
                var button = ButtonFor(page, action);
                var wordings = TooltipWordings(button);

                Assert.True(
                    wordings.Count > 0,
                    $"The '{action}' button has no tooltip explaining that it uses AI.");

                // Every wording, not just the first. A tooltip that changes with
                // the contract's state has more than one, and a branch that forgets
                // to say the model is involved is exactly as misleading as a button
                // with no tooltip at all — it is simply harder to notice.
                foreach (var wording in wordings)
                {
                    Assert.True(
                        wording.Contains("AI", StringComparison.Ordinal),
                        $"The '{action}' tooltip does not mention AI: \"{wording}\"");
                }
            }
        }

        /// <summary>
        /// What a button's tooltip can say — one wording for a plain attribute,
        /// and every branch of a Razor conditional for a tooltip that varies.
        ///
        /// This used to read <c>title="([^"]+)"</c> and take the first match. That
        /// works only while the attribute is a literal: the moment one became
        /// <c>title="@(x is null ? "…" : "…")"</c> the regex stopped at the first
        /// inner quote and the test reported a tooltip of <c>@(x is null ?</c> —
        /// failing on a page where both wordings were perfectly correct.
        /// </summary>
        private static List<string> TooltipWordings(string button)
        {
            var at = button.IndexOf("title=\"", StringComparison.Ordinal);
            if (at < 0) return [];

            var start = at + "title=\"".Length;

            // A plain attribute ends at the next quote.
            if (start >= button.Length || button[start] != '@')
            {
                var end = button.IndexOf('"', start);
                if (end < 0) return [];

                var literal = button[start..end];

                return string.IsNullOrWhiteSpace(literal) ? [] : [literal];
            }

            // A Razor expression ends where its parentheses balance — quotes
            // inside it are the wordings, not the end of the attribute, which is
            // what a quote-to-quote regex got wrong.
            var open = button.IndexOf('(', start);
            if (open < 0) return [];

            var depth = 1;
            var i = open + 1;

            while (i < button.Length && depth > 0)
            {
                if (button[i] == '(') depth++;
                else if (button[i] == ')') depth--;
                i++;
            }

            return Regex.Matches(button[open..i], @"""(?<text>[^""]{4,})""")
                .Select(m => m.Groups["text"].Value)
                .ToList();
        }

        [Fact]
        public void OrdinaryActionsAreNotMarked()
        {
            var page = PositionsPage();

            foreach (var action in OrdinaryActions)
            {
                var button = ButtonFor(page, action, required: false);
                if (button.Length == 0) continue;

                Assert.False(
                    button.Contains("_AiMark", StringComparison.Ordinal) ||
                    button.Contains("wh-ai-action", StringComparison.Ordinal),
                    $"The '{action}' button does not call the model but wears the AI mark, " +
                    "which makes the mark meaningless everywhere else.");
            }
        }

        [Fact]
        public void TheMarkIsExplainedOnceOnThePage()
        {
            var page = PositionsPage();

            Assert.Contains("wh-ai-legend", page);

            // A mark nobody can decode is decoration. The legend has to say both
            // that AI is involved and that its output is a proposal.
            var legend = Between(page, "wh-ai-legend", "</p>");

            Assert.Contains("AI model", legend, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("confirm", legend, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheMarkIsAnnouncedToScreenReaders()
        {
            var mark = File.ReadAllText(Path.Combine(WebRoot, "Pages", "Shared", "_AiMark.cshtml"));

            // The icon is decorative to assistive technology, so the meaning has
            // to arrive as text or it does not arrive at all.
            Assert.Contains("aria-hidden=\"true\"", mark);
            Assert.Contains("visually-hidden", mark);
            Assert.Contains("Uses AI", mark);
        }

        [Fact]
        public void TheMarkIsStyledRatherThanRelyingOnColourAlone()
        {
            var css = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "css", "site.css"));

            Assert.Contains(".wh-ai-mark__icon", css);

            // On a filled button a purple mark would be unreadable against the
            // button's own ground.
            Assert.Contains(".wh-ai-action.btn-primary .wh-ai-mark__icon", css);
        }

        // ---------------------------------------------------------------

        private static string WebRoot => TestPaths.WebProject;

        private static string PositionsPage() =>
            File.ReadAllText(Path.Combine(WebRoot, "Pages", "Contracts", "Positions.cshtml"));

        /// <summary>
        /// The markup of the one button carrying this action, from its opening
        /// tag to its closing tag.
        /// </summary>
        private static string ButtonFor(string page, string action, bool required = true)
        {
            var marker = $"data-action=\"{action}\"";
            var at = page.IndexOf(marker, StringComparison.Ordinal);

            if (at < 0)
            {
                Assert.False(required, $"No button with data-action=\"{action}\" was found on the page.");
                return "";
            }

            var open = page.LastIndexOf("<button", at, StringComparison.Ordinal);
            var close = page.IndexOf("</button>", at, StringComparison.Ordinal);

            Assert.True(open >= 0 && close > open, $"Could not read the '{action}' button's markup.");

            return page[open..close];
        }

        private static string Between(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return "";

            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }
    }
}

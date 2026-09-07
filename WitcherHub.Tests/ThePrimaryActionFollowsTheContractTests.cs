using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The button that writes the contract describes the contract in front of you.
    ///
    /// Both halves of it were decided on the server when the page was drawn and
    /// then never revisited, and adding a position changes both.
    ///
    /// Whether it can be pressed: a contract has no source at the moment it is
    /// created, so the button is drawn disabled. Saving the first position left
    /// it greyed out — the one button the page exists for, dead, with nothing
    /// saying why and nothing to do but reload. That is the first thing anyone
    /// meets on a new contract.
    ///
    /// And what it is called: on a contract with pasted text it reads "Prepare
    /// supplied contract". Adding a position makes it a contract built from both,
    /// and the button went on offering the supplied-text action. Asked for the
    /// one that reads "Generate from text and positions", the page had no such
    /// button on it — measured in a browser, before and after saving:
    ///
    ///     pasted text only          "Prepare supplied contract"
    ///     + a position added        "Prepare supplied contract"
    ///     + saved                   "Prepare supplied contract"
    ///
    /// The names come from ContractSource, which exists so this wording cannot
    /// drift between the browser and the server. The browser therefore picks
    /// between names the server rendered rather than carrying a fourth copy.
    /// </summary>
    public class ThePrimaryActionFollowsTheContractTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        private static string Source(params string[] parts)
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(new[] { root!.FullName }.Concat(parts).ToArray()));
        }

        private static string View() => Source("WitcherHub", "Pages", "Contracts", "Positions.cshtml");

        private static string Builder() =>
            Source("WitcherHub", "wwwroot", "js", "pages", "contracts", "positions-builder.js");

        // ===================================================== it is kept current

        [Fact]
        public void TheButtonIsRecomputedWheneverThePositionsChange()
        {
            var js = Builder();

            // In render(), which runs on add, delete and reorder — not only after
            // a save. A position that exists but has not been saved yet is still
            // a position, and pressing the button saves before it generates.
            Assert.Matches(
                @"updateTotals\(\);\s*refreshPrimaryAction\(\);",
                Regex.Replace(js, @"\s+", " "));
        }

        [Fact]
        public void ItCanBePressedOnceTheContractHasSomethingToWriteFrom()
        {
            var body = MethodBody(Builder(), "function refreshPrimaryAction(");

            Assert.False(string.IsNullOrWhiteSpace(body), "nothing keeps the primary action current");

            // The rule, and the same one the domain states: positions, or
            // supplied text, or both.
            Assert.Contains("disabled = !(hasPositions || hasSuppliedText)", body);
        }

        [Fact]
        public void ItIsNamedForWhatTheContractIsNowBuiltFrom()
        {
            var body = MethodBody(Builder(), "function refreshPrimaryAction(");

            foreach (var branch in new[] { "hybrid", "supplied", "positions" })
                Assert.Contains(branch, body);
        }

        // =============================================== the words stay the domain's

        [Fact]
        public void TheThreeNamesComeFromTheDomainRatherThanBeingRetypedInScript()
        {
            var view = View();

            // Rendered from ContractSource itself, so a change to the label there
            // reaches the browser too.
            foreach (var attribute in new[]
                     {
                         "data-label-positions",
                         "data-label-supplied",
                         "data-label-hybrid"
                     })
            {
                Assert.Contains(attribute, view);
            }

            Assert.Contains("ContractSource.From", view);
            Assert.Contains("PrimaryActionLabel", view);
        }

        [Fact]
        public void ScriptDoesNotCarryItsOwnCopyOfTheLabels()
        {
            // Comments stripped first: the file explains the bug by quoting the
            // wording, and a comment is not a copy that can go stale.
            var js = Regex.Replace(
                Regex.Replace(Builder(), @"/\*.*?\*/", "", RegexOptions.Singleline),
                @"^\s*//.*$", "", RegexOptions.Multiline);

            // The exact wording the owner asked for by name. If it appears here
            // as a literal it will be the copy that goes stale.
            foreach (var wording in new[]
                     {
                         "Generate from text and positions",
                         "Prepare supplied contract",
                         "Generate from positions"
                     })
            {
                Assert.DoesNotContain(wording, js);
            }

            Assert.Contains("app.dataset[\"label\"", Builder());
        }

        // ============================================== nothing else was disturbed

        [Fact]
        public void ALockedContractsButtonIsLeftAlone()
        {
            var body = MethodBody(Builder(), "function refreshPrimaryAction(");

            // A signed contract's controls are disabled deliberately. Recomputing
            // the primary action must not hand one of them back.
            Assert.Contains("locked", body);
            Assert.Matches(@"if \(!generate \|\| locked\) return;", body);
        }

        [Fact]
        public void TheAiMarkSurvivesTheRename()
        {
            var body = MethodBody(Builder(), "function refreshPrimaryAction(");

            // The mark beside the label is an element. Setting textContent on the
            // button would take it with it, and the page's whole promise is that
            // every AI action is marked.
            Assert.Contains("Node.TEXT_NODE", body);
            Assert.DoesNotContain("generate.textContent =", body);
            Assert.DoesNotContain("innerHTML", body);
        }

        // =================================================================== helper

        /// <summary>
        /// The body of a function, brace-matched from its declaration.
        /// </summary>
        private static string MethodBody(string source, string declaration)
        {
            var at = source.IndexOf(declaration, StringComparison.Ordinal);
            if (at < 0) return "";

            var open = source.IndexOf('{', at);
            if (open < 0) return "";

            var depth = 1;
            var i = open + 1;

            while (i < source.Length && depth > 0)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
                i++;
            }

            return source[(open + 1)..(i - 1)];
        }
    }
}

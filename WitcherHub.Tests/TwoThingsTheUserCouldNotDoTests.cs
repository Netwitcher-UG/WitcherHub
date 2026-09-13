using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Two things that could not be done, reported together.
    ///
    ///   * "add checkbox instead of text … to client check it if he accept the
    ///     terms and conditions and after checked can press sign&amp;accept button"
    ///
    ///     Accepting the terms was a sentence of advice — "Please review both
    ///     documents carefully before signing" — sitting where a decision
    ///     belongs. And the one checkbox that was there rendered at 0 x 0,
    ///     measured in a browser: the theme's reset applies `appearance: none`
    ///     to every input on the page, so the signer saw a sentence with no box
    ///     beside it.
    ///
    ///   * "when i add position i cant save it because save position button is
    ///     hide"
    ///
    ///     The Save button was rendered from the positions saved in the database
    ///     when the page was built. On a new contract there are none, so the
    ///     button was never in the page at all — you could add a position and
    ///     then had nothing to save it with.
    /// </summary>
    public class TwoThingsTheUserCouldNotDoTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        private static string Read(params string[] parts)
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(
                new[] { root!.FullName }.Concat(parts).ToArray()));
        }

        private static string SigningPage() => Read("WitcherHub", "Pages", "Contracts", "Sign.cshtml");
        private static string SigningScript() => Read("WitcherHub", "wwwroot", "js", "pages", "contracts", "contract-sign.js");
        private static string SigningStyles() => Read("WitcherHub", "wwwroot", "css", "contracts", "contract-sign.css");
        private static string BuilderPage() => Read("WitcherHub", "Pages", "Contracts", "Positions.cshtml");
        private static string BuilderScript() => Read("WitcherHub", "wwwroot", "js", "pages", "contracts", "positions-builder.js");

        // =================================== accepting the terms is a decision

        [Fact]
        public void TheSignerAcceptsTheTermsInABoxRatherThanBeingToldToReadThem()
        {
            var page = SigningPage();

            Assert.Contains("id=\"chkTerms\"", page);
            Assert.Contains("contractPage_AcceptTermsLine", page);

            // The sentence it replaced.
            Assert.DoesNotContain("contractPage_AgreeHelp", page);
        }

        [Fact]
        public void SignAndAcceptStaysShutUntilBothBoxesAreTicked()
        {
            var js = SigningScript();

            var gate = Regex.Match(js, @"function canSignNow\(\)\s*\{(?<body>[^}]*)\}");
            Assert.True(gate.Success, "the gate on Sign & Accept is gone");

            Assert.Contains("chkAgree.checked", gate.Groups["body"].Value);
            Assert.Contains("termsAccepted()", gate.Groups["body"].Value);
        }

        [Fact]
        public void TickingTheSecondBoxReleasesTheButtonWithoutAReload()
        {
            // Without a change listener the button stays disabled until something
            // else on the page happens to refresh it.
            Assert.Matches(
                @"chkTerms\?\.addEventListener\(""change""",
                SigningScript());
        }

        [Fact]
        public void PressingSignWithTheTermsUntickedIsRefused()
        {
            var js = SigningScript();

            // The disabled button is the first line; this is the second, for a
            // click that reaches the handler anyway.
            var open = Regex.Match(js, @"function openModal\(\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
            Assert.True(open.Success, "the signature pad no longer has an entry point");

            Assert.Contains("termsAccepted()", open.Groups["body"].Value);
            Assert.Contains("mustAcceptTerms", open.Groups["body"].Value);
        }

        [Fact]
        public void ThePageThatSharesThisScriptAndHasNoSecondBoxStillWorks()
        {
            // The quote signing page runs the same script and asks for one
            // confirmation, not two. A page without the box must not be treated
            // as a page whose box is unticked.
            var js = SigningScript();

            var helper = Regex.Match(js, @"function termsAccepted\(\)\s*\{(?<body>[^}]*)\}");
            Assert.True(helper.Success);
            Assert.Contains("!chkTerms || chkTerms.checked", helper.Groups["body"].Value);

            // And it must not be added to the hard guard that returns early.
            var guard = Regex.Match(js, @"if \(!chkAgree \|\|[^\n]*\) return;");
            Assert.True(guard.Success);
            Assert.DoesNotContain("chkTerms", guard.Value);
        }

        [Fact]
        public void ASignedContractShowsBothConfirmationsAsGivenAndLocked()
        {
            var js = SigningScript();

            var signed = Regex.Match(js, @"function setSignedUI\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
            Assert.True(signed.Success);

            Assert.Contains("chkTerms.checked = true", signed.Groups["body"].Value);
            Assert.Contains("chkTerms.disabled = true", signed.Groups["body"].Value);
        }

        [Fact]
        public void ResetClearsBothConfirmations()
        {
            Assert.Matches(
                @"chkAgree\.checked = false;\s*\n\s*if \(chkTerms\) chkTerms\.checked = false;",
                SigningScript());
        }

        [Fact]
        public void AConfirmationBoxIsVisibleOnThePageItIsAskedOn()
        {
            // Measured at 0 x 0 in a browser before this rule: the theme resets
            // `appearance: none` on every input and nothing drew a box.
            var rule = Regex.Match(
                SigningStyles(),
                @"\.contractActions__agree input\[type=""checkbox""\] \{(?<body>[^}]*)\}");

            Assert.True(rule.Success, "the consent box has no styling of its own");

            Assert.Contains("appearance: auto", rule.Groups["body"].Value);
            Assert.Matches(@"width:\s*\d+px", rule.Groups["body"].Value);
            Assert.Matches(@"height:\s*\d+px", rule.Groups["body"].Value);
        }

        [Fact]
        public void TheNewWordingExistsInEveryLanguageThePageOffers()
        {
            foreach (var file in new[] { "SharedResource.resx", "SharedResource.en.resx", "SharedResource.de.resx" })
            {
                var resx = Read("WitcherHub", "Resources", file);

                Assert.Contains("contractPage_AcceptTermsLine", resx);
                Assert.Contains("contractPage_ErrMustAcceptTerms", resx);
            }

            // German is a language the customer switches to, not a fallback to
            // English text under a German flag.
            var de = Read("WitcherHub", "Resources", "SharedResource.de.resx");
            Assert.Contains("Vertragsbedingungen dieses Vertrages", de);
        }

        // ================================== saving the position you just added

        [Fact]
        public void TheSaveButtonIsOnThePageEvenWhenNothingIsSavedYet()
        {
            var page = BuilderPage();

            Assert.Contains("id=\"savePositionsBtn\"", page);

            // It must not be behind a server-side condition again: the page is
            // built once, and the list changes afterwards.
            Assert.DoesNotContain(
                "@if (Model.HasPositions)",
                page);
        }

        [Fact]
        public void ItOpensHiddenOnAContractWithNoPositions()
        {
            Assert.Matches(
                @"id=""savePositionsBtn""[\s\S]{0,200}?Model\.HasPositions \? """" : ""d-none""",
                BuilderPage());
        }

        [Fact]
        public void TheButtonFollowsTheListRatherThanThePageLoad()
        {
            var js = BuilderScript();

            var render = Regex.Match(js, @"function render\(\)\s*\{(?<body>[^}]*)\}");
            Assert.True(render.Success, "the builder no longer has a render pass");

            // Shown while there is something to save, hidden when the last
            // position goes.
            Assert.Contains(
                "saveBtn.classList.toggle(\"d-none\", positions.length === 0)",
                render.Groups["body"].Value);
        }

        [Fact]
        public void ALockedContractHasNoSaveButtonAndTheBuilderDoesNotAssumeOne()
        {
            var js = BuilderScript();

            // The button is not rendered at all on a signed contract, so the
            // lookup has to tolerate its absence.
            Assert.Contains("const saveBtn = document.getElementById(\"savePositionsBtn\")", js);
            Assert.Contains("if (saveBtn) saveBtn.classList.toggle", js);
        }
    }
}

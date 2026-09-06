using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What comes out of the printer when someone presses Print on a contract.
    ///
    /// Reported as: the page's header, the record's status and the command
    /// buttons should not be on the printout — the paper should carry the
    /// contract and nothing else.
    ///
    /// Read off the printed page before this, in order:
    ///
    ///     All contracts
    ///     C-2026-000002
    ///     Awaiting signature
    ///     Unsigned
    ///     Musterfirma GmbH · Online Verkauf
    ///     Positions
    ///     Edit
    ///     Print
    ///     ...and only then the contract
    ///
    /// and it ended with the application's own footer, "© 2026 Netwitcher UG.
    /// All rights reserved. / WitcherHub", printed under a customer's contract.
    /// The sheet was also pushed sideways: the sidebar was hidden but the 275px
    /// of room reserved for it was never given back.
    /// </summary>
    public class PrintingAContractPrintsTheContractTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        /// <summary>
        /// The stylesheet with its comments removed.
        ///
        /// The comments have to go before anything counts braces: this file
        /// explains itself by quoting the rules it is defending against, and
        /// those quotes contain braces. Left in, a brace-matched block ends in
        /// the middle of a paragraph of prose and the parser reads half the
        /// rules — which would let these tests pass while the other half were
        /// gone.
        /// </summary>
        private static string Stylesheet()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            var css = File.ReadAllText(Path.Combine(
                root!.FullName, "WitcherHub", "wwwroot", "css", "contracts", "contract-sign.css"));

            return Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        }

        /// <summary>
        /// Every <c>@media print</c> block in the stylesheet, joined. Brace
        /// matching rather than a regex, because these blocks contain nested
        /// <c>@page</c> rules that a lazy pattern would cut short — and a test
        /// that reads half the rules would pass while the other half were gone.
        /// </summary>
        private static string PrintRules()
        {
            var css = Stylesheet();
            var joined = new System.Text.StringBuilder();

            foreach (Match at in Regex.Matches(css, @"@media\s+print\s*\{"))
            {
                var depth = 1;
                var i = at.Index + at.Length;

                while (i < css.Length && depth > 0)
                {
                    if (css[i] == '{') depth++;
                    else if (css[i] == '}') depth--;
                    i++;
                }

                joined.Append(css[(at.Index + at.Length)..(i - 1)]).Append('\n');
            }

            var text = joined.ToString();
            Assert.False(string.IsNullOrWhiteSpace(text), "the stylesheet has no print rules at all");
            return text;
        }

        /// <summary>The declarations of the first rule whose selector list contains <paramref name="selector"/>.</summary>
        private static string RuleFor(string selector)
        {
            foreach (Match rule in Regex.Matches(PrintRules(), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
            {
                var selectors = rule.Groups["sel"].Value
                    .Split(',')
                    .Select(s => s.Trim());

                if (selectors.Contains(selector))
                    return rule.Groups["body"].Value;
            }

            return "";
        }

        private static bool HiddenInPrint(string selector) =>
            Regex.IsMatch(RuleFor(selector), @"display:\s*none");

        // ================================================ off the paper

        [Fact]
        public void TheRecordsHeaderDoesNotPrint()
        {
            // One element carries the back link, the contract number, the status
            // badge, the signed/unsigned badge, the customer and project links
            // and all three command buttons.
            Assert.True(HiddenInPrint(".wh-page-header"),
                "the administrator's header still prints on the contract");
        }

        [Fact]
        public void TheApplicationsFooterDoesNotPrintUnderTheContract()
        {
            // The rule named `.dashboard-footer`. The layout renders `.d-footer`,
            // so the copyright line printed under every contract while a rule
            // that looked like it handled the case sat right above it.
            Assert.True(HiddenInPrint(".d-footer"),
                "the application footer still prints under the contract");
        }

        [Fact]
        public void NothingYouCanPressIsPrinted()
        {
            Assert.True(HiddenInPrint(".dashboard-main-body .btn"));
            Assert.True(HiddenInPrint(".contractPage .btn"));
        }

        [Fact]
        public void TheChromeAroundThePageIsStillOffThePaper()
        {
            foreach (var selector in new[]
                     {
                         ".sidebar",
                         ".navbar-header",
                         ".contractPage__toast",
                         ".contractPage__langFab",
                         ".contractPaper__pages"
                     })
            {
                Assert.True(HiddenInPrint(selector), selector + " still prints");
            }
        }

        // ================================================ what stays, and where

        [Fact]
        public void TheContractItselfIsNeverHidden()
        {
            // The obvious way to get a clean printout is to hide too much.
            foreach (var selector in new[] { ".contractPage__contractHtml", ".contractPage__paper", ".contractPaper__head" })
            {
                Assert.False(HiddenInPrint(selector), selector + " is hidden on paper — the contract would not print");
            }
        }

        [Fact]
        public void ANoticeKeepsItsWordsAndLosesItsPanel()
        {
            // A preview of an unapproved version prints a line saying so.
            // Hiding it outright would hand someone a draft that looks exactly
            // like the contract; leaving the coloured alert box on would put a
            // piece of interface on a legal document. It keeps the sentence and
            // drops the panel.
            var notice = RuleFor(".dashboard-main-body > .alert");

            Assert.False(string.IsNullOrWhiteSpace(notice), "the notice has no print rule");
            Assert.DoesNotContain("display: none", notice);
            Assert.Matches(@"background:\s*transparent", notice);
            Assert.Matches(@"border:\s*0", notice);
        }

        [Fact]
        public void TheRoomReservedForTheSidebarIsGivenBack()
        {
            // Measured in print media: .dashboard-main kept margin-left 275px
            // with the sidebar hidden, so the contract sat to the right of where
            // the paper starts and lost its right edge.
            var main = RuleFor(".dashboard-main");

            Assert.False(string.IsNullOrWhiteSpace(main), ".dashboard-main has no print rule");
            Assert.Matches(@"margin-left:\s*0", main);
        }

        [Fact]
        public void ThePaperIsNotIndentedByTheApplicationsOwnPadding()
        {
            Assert.Matches(@"padding:\s*0", RuleFor(".dashboard-main-body"));
        }

        // ================================================ blast radius

        [Fact]
        public void NoneOfThisChangesHowThePageLooksOnScreen()
        {
            var css = Stylesheet();
            var print = PrintRules();

            // Every selector this fix introduced belongs to the application
            // shell rather than to the contract, so each one must appear only
            // inside a print block — counted, because the print rules are
            // extracted from the file rather than removed from a copy of it.
            foreach (var selector in new[] { ".wh-page-header", ".d-footer", ".dashboard-main", ".dashboard-main-body .btn" })
            {
                var inPrint = Regex.Matches(print, Regex.Escape(selector)).Count;
                var anywhere = Regex.Matches(css, Regex.Escape(selector)).Count;

                Assert.True(inPrint > 0, selector + " has no print rule");
                Assert.True(anywhere == inPrint,
                    selector + " is also styled outside @media print, which would change the screen");
            }
        }

        [Fact]
        public void TheRulesLiveWhereOnlyContractPagesLoadThem()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            // The pages that pull in this stylesheet. If it is ever added to the
            // shared layout, hiding .wh-page-header in print stops being a
            // contract decision and becomes an application-wide one.
            var loaders = Directory
                .EnumerateFiles(Path.Combine(root!.FullName, "WitcherHub", "Pages"), "*.cshtml", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("contracts/contract-sign.css"))
                .Select(f => Path.GetFileName(Path.GetDirectoryName(f)) + "/" + Path.GetFileName(f))
                .OrderBy(x => x)
                .ToList();

            Assert.All(loaders, name =>
                Assert.True(
                    name is "Contracts/Details.cshtml" or "Contracts/Edit.cshtml"
                         or "Shared/_ContractsLayout.cshtml",
                    name + " now loads the contract print rules — check that hiding the "
                         + "application's header on paper is right for it too"));
        }
    }
}

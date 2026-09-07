using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The contract and the quote, on whatever the customer opens them on.
    ///
    /// Both are laid out as a sheet of A4 with the margins a German business
    /// document is set to — 25mm all round. That is a print measurement, and it
    /// was being spent at every screen size. Measured on a phone:
    ///
    ///     viewport   paper    side padding    the document got
    ///        320px    260px      94px + 94px        160px
    ///        360px    300px      94px + 94px        200px
    ///        390px    330px      94px + 94px        230px
    ///
    /// A contract rendered as a 160px ribbon down the middle of the screen,
    /// 10,269px tall, with the project chip 318px wider than the phone. It did
    /// not scroll sideways only because .contractPage hides its overflow, so
    /// the parts that did not fit were simply not visible.
    ///
    /// The file already carried narrow-screen paper rules. They never applied:
    /// the A4 block is written after them and set the padding unconditionally,
    /// so it won the cascade at every width.
    /// </summary>
    public class TheDocumentFitsTheScreenItIsOpenedOnTests
    {
        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        private static string Styles()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(
                root!.FullName, "WitcherHub", "wwwroot", "css", "contracts", "contract-sign.css"));
        }

        private static string WithoutComments(string css) =>
            Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

        /// <summary>
        /// The body of a <c>@media</c> block whose condition contains
        /// <paramref name="condition"/>, brace-matched rather than pattern-matched
        /// so a nested rule cannot cut it short.
        /// </summary>
        private static string MediaBlock(string condition)
        {
            var css = WithoutComments(Styles());

            foreach (Match at in Regex.Matches(css, @"@media[^{]*" + Regex.Escape(condition) + @"[^{]*\{"))
            {
                var depth = 1;
                var i = at.Index + at.Length;

                while (i < css.Length && depth > 0)
                {
                    if (css[i] == '{') depth++;
                    else if (css[i] == '}') depth--;
                    i++;
                }

                return css[(at.Index + at.Length)..(i - 1)];
            }

            return "";
        }

        // ============================================= the sheet gives way

        [Fact]
        public void TheSheetsPrintMarginIsOnlySpentWhereThereIsASheet()
        {
            var narrow = MediaBlock("max-width: 1000px");
            Assert.False(string.IsNullOrWhiteSpace(narrow),
                "there is no rule stepping the sheet down below a full A4 width");

            var paper = Regex.Match(narrow, @"\.contractPage__paper \{(?<body>[^}]*)\}");
            Assert.True(paper.Success, "the sheet keeps its A4 margins on a narrow screen");

            // 25mm is 94px. Anything of that order leaves a phone with nothing.
            var padding = Regex.Match(paper.Groups["body"].Value, @"padding:\s*(?<v>[^;]+);");
            Assert.True(padding.Success, "the narrow sheet does not state its padding");
            Assert.DoesNotContain("25mm", padding.Groups["v"].Value);
        }

        [Fact]
        public void ThatRuleComesAfterTheOneItHasToBeat()
        {
            // The file already had narrow-screen paper rules before this fix and
            // they did nothing, because the A4 block is written after them and
            // set padding unconditionally. Order is the whole point.
            var css = WithoutComments(Styles());

            var a4 = css.IndexOf("padding: 25mm 25mm 30mm", StringComparison.Ordinal);
            var narrow = css.IndexOf("@media (max-width: 1000px)", StringComparison.Ordinal);

            Assert.True(a4 >= 0, "the A4 sheet rule is gone");
            Assert.True(narrow > a4,
                "the narrow-screen sheet rule is written before the A4 block, so the "
                + "A4 padding wins the cascade again and phones go back to a 160px ribbon");
        }

        [Fact]
        public void APhoneGetsTheDocumentRatherThanTheMargins()
        {
            var phone = MediaBlock("max-width: 640px");
            Assert.False(string.IsNullOrWhiteSpace(phone), "there is no phone rule at all");

            var paper = Regex.Match(phone, @"\.contractPage__paper \{(?<body>[^}]*)\}");
            Assert.True(paper.Success);

            var px = Regex.Match(paper.Groups["body"].Value, @"padding:\s*\d+px\s+(?<side>\d+)px");
            Assert.True(px.Success, "the phone sheet's side padding is not a plain pixel value");
            Assert.True(int.Parse(px.Groups["side"].Value) <= 20,
                "a phone still spends more than 20px a side on margins");
        }

        [Fact]
        public void TheDocumentIsNotPulledWiderThanASheetThatHasNothingToGiveBack()
        {
            // On a desktop the document is pulled 12mm past the sheet on each
            // side, because the sheet has 25mm to spare. A narrow sheet does not.
            var narrow = MediaBlock("max-width: 1000px");

            Assert.Matches(
                @"\.contractPage__contractHtml\.contractPdfScope,\s*"
                + @"\.contractPage__contractHtml\.quotePdfScope \{[^}]*margin-left:\s*0",
                narrow);
        }

        // ============================================= what stacks on a phone

        [Fact]
        public void TheDocumentsTwoColumnPartsBecomeOneColumn()
        {
            var phone = MediaBlock("max-width: 640px");

            foreach (var part in new[] { ".header", ".summary-banner", ".party-grid" })
                Assert.Contains(part, phone);

            // Label above value: "Name" and its box cannot share 200px.
            Assert.Matches(@"\.contractSign__row \{[^}]*grid-template-columns:\s*1fr", phone);
        }

        [Fact]
        public void TheTitleIsGivenASizeItFitsIn()
        {
            var phone = MediaBlock("max-width: 640px");

            // "Agenturvertrag" at 27px needs 190px and had about 150 beside the
            // logo, so the customer's contract was headed "Agenturvert".
            var title = Regex.Match(phone,
                @"\.contractPdfScope \.title-block h1,\s*\.quotePdfScope \.title-block h1 \{(?<body>[^}]*)\}");

            Assert.True(title.Success, "the title keeps its desktop size on a phone");
            Assert.Matches(@"font-size:\s*\d+px\s*!important", title.Groups["body"].Value);
            Assert.Contains("overflow-wrap: break-word", title.Groups["body"].Value);
        }

        [Fact]
        public void ATableTooWideForThePhoneScrollsInsteadOfBeingCutOff()
        {
            var tables = MediaBlock("max-width: 700px");
            Assert.False(string.IsNullOrWhiteSpace(tables), "no rule handles a table on a phone");

            Assert.Contains("overflow-x: auto", tables);
            Assert.Contains(".contractPdfScope .price-box", tables);
            Assert.Contains(".quotePdfScope .price-box", tables);
        }

        [Fact]
        public void TheLanguageSwitchIsSizedLikeAControl()
        {
            // Measured at 39x30 — under the height a thumb can rely on.
            Assert.Matches(
                @"\.contractPage__langBtn \{[^}]*min-height:\s*3[2-9]px",
                WithoutComments(Styles()));
        }

        // ============================================= nothing regressed

        [Fact]
        public void ADesktopStillGetsTheDocumentSetAsADocument()
        {
            var css = WithoutComments(Styles());

            // The A4 sheet and its 25mm margins are the default and stay the
            // default; only narrower screens step away from them.
            Assert.Contains("width: 210mm", css);
            Assert.Contains("padding: 25mm 25mm 30mm", css);
        }

        [Fact]
        public void TheSheetIsStillNeverClipped()
        {
            // A long contract must not be made to look like a short one, which
            // is what an overflow rule on the sheet would do.
            foreach (Match m in Regex.Matches(
                         WithoutComments(Styles()),
                         @"\.contractPage__paper \{(?<body>[^}]*)\}"))
            {
                Assert.DoesNotContain("overflow: hidden", m.Groups["body"].Value);
            }
        }

        [Fact]
        public void LabelsAndCaptionsAreNotJustifiedLikeTheContractsProse()
        {
            var css = WithoutComments(Styles());

            // The document is set justified, which is right for its clauses and
            // wrong for a chip or a one-line caption: once those wrapped they
            // came out as "Alle   Positionen   und   Details".
            foreach (var sel in new[]
                     {
                         ".quotePdfScope .chip",
                         ".quotePdfScope .section-head p",
                         ".contractPdfScope .chip",
                         ".contractPdfScope .section-head p"
                     })
            {
                Assert.Contains(sel, css);
            }
        }
    }
}

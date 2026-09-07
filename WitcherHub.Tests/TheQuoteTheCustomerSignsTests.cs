using System.Text.RegularExpressions;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The quote as the customer receives it — from the e-mail link or from
    /// Copy link, both in Quote details — and what either signing page tells
    /// them when signing fails.
    ///
    /// Measured in a browser on a quote with three positions and real German
    /// titles:
    ///
    ///   * "Hinweise" and "Finanzübersicht" rendered at 48px, nearly twice the
    ///     document's own title. The heading rules written for the contract
    ///     never named the quote's two panels, so the admin theme was still
    ///     setting them;
    ///   * the chip row ran 91px past the edge of the sheet, so the project
    ///     name and the validity date were cut off the page;
    ///   * the positions table broke words wherever the line happened to end:
    ///     columns headed "Po s." and "Me nge", cells reading "Warenwirtsc
    ///     haft" and "Monatli ch";
    ///   * the column widths added up to 110%, and the money columns were the
    ///     narrowest of them;
    ///   * and when signing failed the customer was told "Something went
    ///     wrong. Please try again." even when the server had said exactly
    ///     what was wrong.
    /// </summary>
    public class TheQuoteTheCustomerSignsTests
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

        private static string Template() =>
            Read("WitcherHub.Infrastructure", "Services", "Pdf", "QuotePdfHtmlBuilder.cs");

        private static string SigningScript() =>
            Read("WitcherHub", "wwwroot", "js", "pages", "contracts", "contract-sign.js");

        private static string SigningStyles() =>
            Read("WitcherHub", "wwwroot", "css", "contracts", "contract-sign.css");

        /// <summary>The body of the first rule in the quote template matching <paramref name="selector"/>.</summary>
        private static string RuleFor(string selector)
        {
            var css = Regex.Replace(Template(), @"/\*.*?\*/", "", RegexOptions.Singleline);

            var m = Regex.Match(css, Regex.Escape(selector) + @"\s*\{(?<body>[^{}]*)\}");
            return m.Success ? m.Groups["body"].Value : "";
        }

        // ================================================== how the quote reads

        [Fact]
        public void TheQuotesOwnPanelsAreSizedLikeHeadingsRatherThanBanners()
        {
            var css = Regex.Replace(SigningStyles(), @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Measured at 48px before this: the theme sets every h3 with
            // !important, and these two were not among the selectors that took
            // that decision back for the document.
            Assert.Contains(".quotePdfScope .notes h3", css);
            Assert.Contains(".quotePdfScope .totals h3", css);

            var rule = Regex.Match(css,
                @"\.quotePdfScope \.notes h3,\s*\.quotePdfScope \.totals h3 \{(?<body>[^}]*)\}");

            Assert.True(rule.Success, "the quote's panel headings have no rule of their own");
            Assert.Matches(@"font-size:\s*[\d.]+px\s*!important", rule.Groups["body"].Value);
        }

        [Fact]
        public void TheChipsWrapInsteadOfRunningOffTheSheet()
        {
            var chipRow = RuleFor(".chip-row");
            Assert.False(string.IsNullOrWhiteSpace(chipRow), "the chip row rule is gone");

            Assert.Contains("flex-wrap: wrap", chipRow);
            Assert.DoesNotContain("nowrap", chipRow);

            // And a single chip may shrink: the project chip carries the whole
            // project title, and `flex: 0 0 auto` would not let it.
            var chip = RuleFor(".chip");
            Assert.DoesNotContain("flex: 0 0 auto", chip);
            Assert.Matches(@"max-width:\s*100%", chip);
        }

        [Fact]
        public void AColumnHeadingIsNeverCutInsideAWord()
        {
            var head = RuleFor("thead th");
            Assert.False(string.IsNullOrWhiteSpace(head), "the table heading rule is gone");

            // `overflow-wrap: anywhere` gave the customer columns headed
            // "Po s." and "Me nge".
            Assert.DoesNotContain("anywhere", head);
            Assert.Contains("overflow-wrap: normal", head);
        }

        [Fact]
        public void APositionsTextBreaksOnlyWhenTheWordAloneIsTooWide()
        {
            var cell = RuleFor("tbody td");
            Assert.False(string.IsNullOrWhiteSpace(cell));

            Assert.DoesNotContain("anywhere", cell);
            Assert.Contains("overflow-wrap: break-word", cell);
            Assert.Contains("hyphens: auto", cell);
        }

        [Fact]
        public void TheColumnsAddUpToOneTable()
        {
            var css = Template();

            var widths = Regex.Matches(css, @"\.w-[a-z]+ \{ width: (?<pc>\d+)%; \}")
                .Select(m => int.Parse(m.Groups["pc"].Value))
                .ToList();

            Assert.True(widths.Count >= 9, "the quote table lost its column widths");

            // They summed to 110, so the browser rescaled every one of them and
            // the money columns — already the narrowest — came out too narrow
            // to hold a five-figure amount.
            Assert.Equal(100, widths.Sum());
        }

        [Fact]
        public void TheAmountsGetMoreRoomThanTheSingleDigitColumns()
        {
            var css = Template();

            int Width(string name) =>
                int.Parse(Regex.Match(css, @"\." + name + @" \{ width: (?<pc>\d+)%; \}").Groups["pc"].Value);

            Assert.True(Width("w-total") > Width("w-pos"));
            Assert.True(Width("w-total") > Width("w-tax"));
            Assert.True(Width("w-price") > Width("w-qty"));
        }

        [Fact]
        public void TheTableKeepsAWidthItCanBeReadAtAndScrollsIfItMustOnScreen()
        {
            var table = RuleFor("table");
            Assert.Matches(@"min-width:\s*\d+px", table);

            var wrap = RuleFor(".table-wrap");
            Assert.Contains("overflow-x: auto", wrap);

            // Paper does not scroll, so print drops both.
            var print = Regex.Match(Template(), @"@media print \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
            Assert.True(print.Success, "the quote template has no print rules");
            Assert.Matches(@"table \{\s*min-width: 0;", print.Groups["body"].Value);
        }

        [Fact]
        public void TheQuoteIsSetInAFaceEveryMachineThatRendersItHas()
        {
            var stack = Regex.Match(Template(), @"font-family:\s*(?<stack>[^;]*);");
            Assert.True(stack.Success);

            Assert.DoesNotContain("Inter", stack.Groups["stack"].Value);
            Assert.Contains("DejaVu Sans", stack.Groups["stack"].Value);
        }

        // ============================== what a failed signature actually says

        [Fact]
        public void AFailedSignatureSaysWhatWentWrongWhenTheServerSaid()
        {
            var js = SigningScript();

            // One script drives both signing pages, so this covers the quote
            // and the contract together.
            var mapper = Regex.Match(js, @"const fromServer = (?<expr>.*?);", RegexOptions.Singleline);
            Assert.True(mapper.Success, "the server's reason is not read at all");

            Assert.Contains("json.message", mapper.Groups["expr"].Value);

            // And it must be reached before the generic line, not after it.
            var body = js[js.IndexOf("const fromServer", StringComparison.Ordinal)..];
            var serverAt = body.IndexOf("fromServer ? fromServer", StringComparison.Ordinal);
            var genericAt = body.IndexOf("i18n.genericError", StringComparison.Ordinal);

            Assert.True(serverAt >= 0, "the server's reason is read but never used");
            Assert.True(serverAt < genericAt,
                "the generic line still wins over what the server said");
        }

        [Fact]
        public void TranslatedWordingStillWinsWhereThereIsSome()
        {
            var js = SigningScript();
            var body = js[js.IndexOf("const fromServer", StringComparison.Ordinal)..];

            // 401, 409 and 404 have wording in the reader's own language. That
            // beats the server's one line of English, so those are checked
            // before falling back to it.
            var unauthorizedAt = body.IndexOf("i18n.unauthorized", StringComparison.Ordinal);
            var serverAt = body.IndexOf("fromServer ? fromServer", StringComparison.Ordinal);

            Assert.True(unauthorizedAt >= 0 && unauthorizedAt < serverAt,
                "the server's English would replace the translated wording");
        }

        [Fact]
        public void TheReasonIsShownWhereItCanBeReadAndKeepsItsLineBreaks()
        {
            var css = Regex.Replace(SigningStyles(), @"/\*.*?\*/", "", RegexOptions.Singleline);

            var status = Regex.Match(css, @"\.contractPage__modalStatus \{(?<body>[^}]*)\}");
            Assert.True(status.Success, "the signing dialog has nowhere to put a reason");

            // The expired-quote message is two sentences with a break between
            // them; collapsed, it reads as one run-on line.
            Assert.Contains("white-space: pre-line", status.Groups["body"].Value);

            var size = Regex.Match(status.Groups["body"].Value, @"font-size:\s*(?<px>[\d.]+)px");
            Assert.True(size.Success && double.Parse(size.Groups["px"].Value,
                System.Globalization.CultureInfo.InvariantCulture) >= 14,
                "the only explanation of why signing failed is set at caption size");
        }

        [Fact]
        public void NoSigningFailureHandsTheCustomerAnExceptionMessage()
        {
            // The reasons are shown now, so they must all be written for a
            // customer. A raw exception message on a public signing page would
            // put internals in front of whoever holds the link.
            foreach (var page in new[] { "Quotes", "Contracts" })
            {
                var source = Read("WitcherHub", "Pages", page, "Sign.cshtml.cs");

                var sign = Regex.Match(source,
                    @"OnPostSignAsync(?<body>.*?)\n        public ",
                    RegexOptions.Singleline);

                if (!sign.Success) continue;

                Assert.DoesNotContain("message = ex.Message", sign.Groups["body"].Value);
                Assert.DoesNotContain("message = ex.ToString()", sign.Groups["body"].Value);
            }
        }

        [Fact]
        public void TheReasonsThatReachTheCustomerAreWordedForOne()
        {
            foreach (var page in new[] { "Quotes", "Contracts" })
            {
                var source = Read("WitcherHub", "Pages", page, "Sign.cshtml.cs");

                // "Invalid signature data." is a note to a developer.
                Assert.DoesNotContain("\"Invalid signature data.\"", source);
            }
        }
    }
}

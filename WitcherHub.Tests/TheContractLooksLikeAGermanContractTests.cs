using WitcherHub.Infrastructure.Services.Pdf;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What the exported contract looks like on paper.
    ///
    /// The template it replaced was a violet-accented card with a gradient
    /// banner, 28px corner radii and a 50px drop shadow — a document that read
    /// as marketing material with a price in it. A German B2B contract is ink on
    /// paper: one typeface, a clear hierarchy, grey rules, and margins a person
    /// can punch holes in.
    ///
    /// These read the generated HTML rather than the PDF, because that is where
    /// the decisions are. The PDF itself was checked by rendering it: five
    /// contracts — German, Arabic-sourced, English-sourced, mixed and a
    /// fourteen-page one — through the application's own path, with the text
    /// extracted from every page and the pages rasterised and looked at.
    /// </summary>
    public class TheContractLooksLikeAGermanContractTests
    {
        private static string Html(bool signaturePlaceholder = true) =>
            ContractPdfHtmlBuilder.Build(new ContractPdfHtmlBuilder.ContractPdfDocumentModel
            {
                ContractNo = "C-4711",
                Currency = "EUR",
                ProjectTitle = "Online Verkauf Verwaltung",
                Provider = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Netwitcher UG" },
                Customer = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Musterfirma GmbH" },
                TermsSectionHtml = "<h2>1. Leistungsbeschreibung</h2><p>Text.</p>",
                ShowSignaturePlaceholder = signaturePlaceholder
            });

        // ================================================================ type

        [Fact]
        public void TheContractIsSetInAFaceThePrinterActuallyHas()
        {
            var css = Html();

            // Arial first; Liberation Sans behind it because it is
            // metric-compatible, so the Linux container that renders the PDF
            // sets the same measure rather than reflowing the document. The
            // stack this replaced led with Segoe UI and Roboto, neither of which
            // exists there, and fell through to whatever was left.
            Assert.Contains("font-family: Arial", css);
            Assert.Contains("\"Liberation Sans\"", css);
            Assert.Contains("\"DejaVu Sans\"", css);
        }

        [Fact]
        public void SizesAreInPointsBecauseTheDestinationIsPaper()
        {
            var css = Html();

            Assert.Contains("font-size: 10.5pt", css);
            Assert.Contains("font-size: 16pt", css);
            Assert.Contains("font-size: 12.5pt", css);
            Assert.Contains("line-height: 1.45", css);
        }

        [Fact]
        public void ThePageIsA4WithABindingMargin()
        {
            var css = Html();

            // Two @page rules — the screen one and the print one — and the print
            // one is what the renderer takes, so both must say the same thing.
            Assert.Equal(2, Occurrences(css, "margin: 20mm 18mm 20mm 22mm;"));
            Assert.Contains("size: A4;", css);
        }

        [Fact]
        public void TheOrnamentIsGone()
        {
            var css = Html();

            Assert.DoesNotContain("linear-gradient", css);
            Assert.DoesNotContain("radial-gradient", css);

            // The violet the template was built around.
            foreach (var purple in new[] { "#7c3aed", "#6d28d9", "#5b21b6", "#2e1065", "#f3e8ff" })
                Assert.DoesNotContain(purple, css);
        }

        [Fact]
        public void TheSummaryBannerIsReadableOnPaper()
        {
            var css = Html();

            // The banner was white text on a dark violet gradient. Neutralising
            // the background without the text left the project name, the parties
            // and the contract total as white on near-white: present in the file
            // and invisible on the page.
            var banner = Between(css, ".summary-banner {", "}");

            Assert.Contains("color: #111111", banner);
            Assert.DoesNotContain("color: #fff", banner);
        }

        // ========================================================== pagination

        [Fact]
        public void AHeadingIsNeverTheLastThingOnAPage()
        {
            var css = Html();

            Assert.Contains("break-after: avoid", css);
            Assert.Contains("page-break-after: avoid", css);

            // And the line after it does not land alone either, which is the
            // same defect one line later.
            Assert.Contains("orphans: 3", css);
            Assert.Contains("widows: 3", css);
        }

        [Fact]
        public void APriceTableThatSpansPagesRepeatsItsHeader()
        {
            var css = Html();

            Assert.Contains("thead { display: table-header-group; }", css);
            Assert.Contains(".rich-text tr    { break-inside: avoid;", css);
        }

        [Fact]
        public void MoneyIsRightAlignedAndNeverBroken()
        {
            var money = Between(css: Html(), from: ".rich-text td:last-child,", to: "}");

            Assert.Contains("text-align: right", money);

            // "1.900,00" ending one line and "EUR" starting the next is a figure
            // the reader has to reassemble.
            Assert.Contains("white-space: nowrap", money);
        }

        [Fact]
        public void ShortLogicalUnitsAreNotTornAcrossAPage()
        {
            var css = Html();

            foreach (var kept in new[]
                     {
                         ".signature-placeholder,", ".signature-grid,", ".signature-box,",
                         ".contract-note,", ".price-box,"
                     })
            {
                Assert.Contains(kept, css);
            }
        }

        [Fact]
        public void TextIsNotJustified()
        {
            // Chromium justifies by stretching word spaces and has no German
            // hyphenation dictionary here, so justified German compounds open
            // rivers of white down the page.
            Assert.Contains("text-align: left;", Between(Html(), ".rich-text,", "}"));
        }

        // ========================================================== signatures

        [Fact]
        public void ThereIsRoomToSignAndTheRuleIsUnderIt()
        {
            var html = Html();

            // The block used to draw its rule above the names with the empty
            // space below: a line at the top of the box asking somebody to sign
            // above a label they had not read yet.
            var role = html.IndexOf("signature-role", StringComparison.Ordinal);
            var party = html.IndexOf("signature-party", StringComparison.Ordinal);
            var rule = html.IndexOf("signature-rule", StringComparison.Ordinal);
            var caption = html.IndexOf("signature-caption", StringComparison.Ordinal);

            Assert.True(role < party && party < rule && rule < caption,
                "Name, then the space to sign in, then the rule, then what the rule is for.");

            Assert.Contains("margin-top: 20mm;", Between(html, ".signature-rule {", "}"));
            Assert.Contains("Ort, Datum, Unterschrift", html);
        }

        [Fact]
        public void ASignedCopyHasNoEmptySignatureBlock()
        {
            // The markup, not the stylesheet: the rule stays defined, the block
            // is simply not drawn on a copy that already carries a signature.
            Assert.DoesNotContain("class=\"signature-rule\"", Html(signaturePlaceholder: false));
            Assert.Contains("class=\"signature-rule\"", Html());
        }

        // ================================================ nothing is said twice

        [Fact]
        public void TheTemplateDrawsNoPriceBoxWhenTheDocumentCarriesItsOwn()
        {
            // A composed contract has its own "2. Vergütung" with the table
            // computed in code. The template's Preisübersicht box used to be
            // drawn regardless, so the PDF stated the same totals twice under
            // two headings — the same defect as a PDF containing two documents.
            var html = Html();

            Assert.DoesNotContain("Preisübersicht", html);
            Assert.DoesNotContain("Anlage A – Leistungsbeschreibung", html);
            Assert.DoesNotContain("<h2>Vertragsgegenstand</h2>", html);
        }

        [Fact]
        public void TheTemplateStillDrawsThemForADocumentThatHasNoneOfItsOwn()
        {
            var html = ContractPdfHtmlBuilder.Build(new ContractPdfHtmlBuilder.ContractPdfDocumentModel
            {
                ContractNo = "C-4711",
                Provider = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Netwitcher UG" },
                Customer = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Musterfirma GmbH" },
                ContractIntroHtml = "<p>Gegenstand.</p>",
                ServicesSectionHtml = "<p>Leistungen.</p>",
                PriceBoxHtml = "<table><tr><td>1.900,00 EUR</td></tr></table>"
            });

            // Contracts stored before the composer existed are still rendered
            // from their own wording, and they rely on these.
            Assert.Contains("<h2>Vertragsgegenstand</h2>", html);
            Assert.Contains("Anlage A – Leistungsbeschreibung", html);
            Assert.Contains("Preisübersicht", html);
        }

        // =================================================================

        private static int Occurrences(string text, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }

        private static string Between(string css, string from, string to)
        {
            var start = css.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, $"No rule beginning “{from}”.");

            var end = css.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end >= 0);

            return css[start..end];
        }
    }
}

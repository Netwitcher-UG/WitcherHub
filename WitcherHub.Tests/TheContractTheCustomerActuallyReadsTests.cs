using System.Reflection;
using System.Text.RegularExpressions;
using WitcherHub.Infrastructure.Services.Pdf;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The contract as the customer receives it, from the link in the e-mail or
    /// from Copy link.
    ///
    /// Reported as two things — it is not readable, and the terms behind the
    /// "I have read the terms" checkbox are not shown — and both were literally
    /// true when the page was opened and measured:
    ///
    ///   * the signing layout loads the admin theme, and that theme sets
    ///     h1..h6 with !important. The document template's own sizes lost, so
    ///     the title rendered at 72px and was clipped to "AGENTURVERTR", section
    ///     headings at 60px, party names wrapped over four lines — while the
    ///     clauses themselves stayed at 13px;
    ///   * the page was assembled from three named slices of the contract and
    ///     dropped everything else. Term, payment, liability, confidentiality,
    ///     data protection, rights of use and governing law were not on the page
    ///     the signature was given on.
    ///
    /// These tests hold both ends: the sizes the document sets for itself, and
    /// the fact that every clause reaches the page.
    /// </summary>
    public class TheContractTheCustomerActuallyReadsTests
    {
        // =============================================== every clause is shown

        private const string AContract = """
# Agenturvertrag

Vertragsnummer C-2026-000002

## Vertragsgegenstand

(1) Der Auftragnehmer erbringt die in Anlage A beschriebenen Leistungen.

## Anlage A – Leistungsbeschreibung

### SEO Betreuung

Laufende Optimierung der Sichtbarkeit.

## Preisübersicht

Die Vergütung ergibt sich aus den vereinbarten Positionen.

## § 1 Vertragslaufzeit und Kündigung

(1) Der Vertrag läuft auf unbestimmte Zeit.

## § 7 Haftung

(1) Der Auftragnehmer haftet unbeschränkt für Vorsatz und grobe Fahrlässigkeit.

## § 8 Schlussbestimmungen

(1) Es gilt das Recht der Bundesrepublik Deutschland.
""";

        /// <summary>
        /// The page model's own splitter, called directly. It is private because
        /// nothing outside the page needs it, but what it decides is the
        /// difference between a signature given on the contract and a signature
        /// given on three paragraphs of it.
        /// </summary>
        private static string RemainingTerms(string? markdown)
        {
            var method = typeof(WitcherHub.Pages.Contracts.SignModel)
                .GetMethod(
                    "ExtractRemainingTermsMarkdown",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            return (string)method!.Invoke(
                null,
                [markdown, new[] { "Vertragsgegenstand", "Anlage A", "Preisübersicht" }])!;
        }

        [Fact]
        public void EveryClauseTheSignatureIsGivenOnReachesThePage()
        {
            var remaining = RemainingTerms(AContract);

            Assert.Contains("§ 1 Vertragslaufzeit und Kündigung", remaining);
            Assert.Contains("§ 7 Haftung", remaining);
            Assert.Contains("§ 8 Schlussbestimmungen", remaining);

            // Not just the headings — the text under them.
            Assert.Contains("auf unbestimmte Zeit", remaining);
            Assert.Contains("Recht der Bundesrepublik Deutschland", remaining);
        }

        [Fact]
        public void TheSectionsThePageAlreadyShowsAreNotShownTwice()
        {
            var remaining = RemainingTerms(AContract);

            Assert.DoesNotContain("## Vertragsgegenstand", remaining);
            Assert.DoesNotContain("## Anlage A", remaining);
            Assert.DoesNotContain("## Preisübersicht", remaining);

            // And not their bodies either.
            Assert.DoesNotContain("in Anlage A beschriebenen Leistungen", remaining);
            Assert.DoesNotContain("Laufende Optimierung", remaining);
        }

        [Fact]
        public void TheTitleBlockIsNotRepeatedInsideTheDocument()
        {
            var remaining = RemainingTerms(AContract);

            // The page carries the title and the contract number in its
            // letterhead. Printing them again at the top of the clauses would
            // read as the document starting over.
            Assert.DoesNotContain("# Agenturvertrag", remaining);
            Assert.DoesNotContain("Vertragsnummer C-2026-000002", remaining);
        }

        [Fact]
        public void AClauseWithAnUnfamiliarHeadingIsStillShown()
        {
            // Nothing is filtered by what the heading says. A contract that
            // numbers its clauses "Artikel 4" rather than "§ 4", or carries a
            // section this code has never seen, is still a contract.
            var remaining = RemainingTerms("""
## Vertragsgegenstand

Die Leistungen.

## Artikel 4 – Besondere Vereinbarungen

Der Auftraggeber erhält ein Sonderkündigungsrecht.
""");

            Assert.Contains("Artikel 4", remaining);
            Assert.Contains("Sonderkündigungsrecht", remaining);
        }

        [Fact]
        public void ADocumentWithNothingBeyondItsSubjectMatterGrowsNoEmptyHeading()
        {
            var remaining = RemainingTerms("""
# Agenturvertrag

## Vertragsgegenstand

Die Leistungen.
""");

            Assert.True(string.IsNullOrWhiteSpace(remaining));
        }

        [Fact]
        public void AContractWithNoWordingAtAllIsHandledRatherThanThrown()
        {
            Assert.Equal("", RemainingTerms(null));
            Assert.Equal("", RemainingTerms("   "));
        }

        // ================================================ the rendered section

        private static ContractPdfHtmlBuilder.ContractPdfDocumentModel AModel(
            string termsHtml = "") => new()
            {
                ContractNo = "C-2026-000002",
                ProjectTitle = "Online Verkauf",
                StatusText = "Sent",
                CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                Provider = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Netwitcher UG" },
                Customer = new ContractPdfHtmlBuilder.ContractPdfParty { Name = "Musterfirma GmbH" },
                ContractIntroHtml = "<p>Die Leistungen.</p>",
                ServicesSectionHtml = "<p>Anlage A.</p>",
                TermsSectionHtml = termsHtml,
                PriceBoxHtml = "<table></table>"
            };

        [Fact]
        public void TheClausesAreRenderedUnderAHeadingOfTheirOwn()
        {
            var html = ContractPdfHtmlBuilder.Build(
                AModel("<h2>§ 7 Haftung</h2><p>Der Auftragnehmer haftet.</p>"));

            Assert.Contains("Vertragsbedingungen", html);
            Assert.Contains("§ 7 Haftung", html);
            Assert.Contains("Der Auftragnehmer haftet.", html);
        }

        [Fact]
        public void TheClauseSectionCarriesTheAnchorTheConsentLinePointsAt()
        {
            var html = ContractPdfHtmlBuilder.Build(AModel("<p>Eine Klausel.</p>"));

            Assert.Contains("id=\"vertragsbedingungen\"", html);
        }

        [Fact]
        public void NoClausesMeansNoEmptySection()
        {
            var html = ContractPdfHtmlBuilder.Build(AModel(""));

            Assert.DoesNotContain("Vertragsbedingungen", html);
            Assert.DoesNotContain("id=\"vertragsbedingungen\"", html);
        }

        [Fact]
        public void TheOpeningScreenDoesNotSayTheSameThingTwice()
        {
            var html = ContractPdfHtmlBuilder.Build(AModel());

            // The summary sentence belongs under the title. The banner used to
            // repeat it word for word, one screen apart.
            var summary = "Dieser Vertrag regelt die vereinbarten Leistungen";
            Assert.Single(Regex.Matches(html, Regex.Escape(summary)));

            // What the banner says instead.
            Assert.Contains("Zwischen Netwitcher UG und Musterfirma GmbH", html);
        }

        // ================================================== how it is typeset

        private static string Template()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(
                root!.FullName,
                "WitcherHub.Infrastructure", "Services", "Pdf", "ContractPdfHtmlBuilder.cs"));
        }

        private static string SigningStylesheet()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            return File.ReadAllText(Path.Combine(
                root!.FullName,
                "WitcherHub", "wwwroot", "css", "contracts", "contract-sign.css"));
        }

        private static DirectoryInfo? RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            return directory;
        }

        [Fact]
        public void TheThemeStillForcesEveryHeadingSizeOnEveryPageItTouches()
        {
            // The canary. The stylesheet below defends the document's own sizes
            // with !important, which is only justified while this is true. If a
            // theme upgrade drops these rules, that defence can be simplified —
            // and this test is where that news arrives.
            var root = RepositoryRoot();
            Assert.NotNull(root);

            var theme = File.ReadAllText(Path.Combine(
                root!.FullName, "WitcherHub", "wwwroot", "wowdash", "css", "style.css"));

            Assert.Matches(@"h1,\s*\.h1\s*\{[^}]*font-size:\s*var\(--h1\)\s*!important", theme);
            Assert.Matches(@"h2,\s*\.h2\s*\{[^}]*font-size:\s*var\(--h2\)\s*!important", theme);
        }

        [Fact]
        public void TheDocumentSetsItsOwnHeadingSizesAndCanWin()
        {
            var css = SigningStylesheet();

            // Scoped to the document, and !important — the only thing the
            // theme's !important yields to.
            Assert.Matches(
                @"\.contractPdfScope \.title-block h1[^}]*font-size:\s*[\d.]+px\s*!important",
                css);

            Assert.Matches(
                @"\.contractPdfScope \.section-head h2[^}]*font-size:\s*[\d.]+px\s*!important",
                css);

            // The quote signing page is the same layout under the same theme, so
            // it is covered by the same rules rather than left broken.
            Assert.Contains(".quotePdfScope .title-block h1", css);
            Assert.Contains(".quotePdfScope .section-head h2", css);
        }

        [Fact]
        public void TheSignaturesHeadingDoesNotDwarfTheFieldsUnderIt()
        {
            Assert.Matches(
                @"\.contractSign__title\s*\{[^}]*font-size:\s*\d+px\s*!important",
                SigningStylesheet());
        }

        [Fact]
        public void TheContractProseIsSetAtAReadingSize()
        {
            var template = Template();

            var rule = Regex.Match(template, @"\.rich-text p \{(?<body>[^}]*)\}");
            Assert.True(rule.Success, "the paragraph rule for contract prose is gone");

            var size = Regex.Match(rule.Groups["body"].Value, @"font-size:\s*(?<px>[\d.]+)px");
            Assert.True(size.Success, "contract prose no longer states its size");

            // It was 13px — caption size — for the one thing on the page the
            // customer has to read.
            Assert.True(
                double.Parse(size.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 15,
                "contract prose is set smaller than reading size");
        }

        [Fact]
        public void AGermanCompoundIsNotBrokenWhereverTheLineHappensToEnd()
        {
            var rule = Regex.Match(Template(), @"\.rich-text p \{(?<body>[^}]*)\}");
            Assert.True(rule.Success);

            // `overflow-wrap: anywhere` split "Umsatzsteuerbehandlung" at
            // whatever character reached the margin. Hyphenation breaks it where
            // the language says it may be broken.
            Assert.DoesNotContain("anywhere", rule.Groups["body"].Value);
            Assert.Contains("hyphens: auto", rule.Groups["body"].Value);
        }

        [Fact]
        public void TheHeaderDoesNotReserveItsGutterByHand()
        {
            var template = Template();

            var header = Regex.Match(template, @"\n\s*\.header \{(?<body>[^}]*)\}");
            Assert.True(header.Success, "the header rule is gone");

            // The old header was a relatively positioned box with the reference
            // card placed absolutely on top of it and a right padding guessed by
            // hand. The guess did not match the card, so the title ran under it.
            Assert.Contains("display: grid", header.Groups["body"].Value);
            Assert.DoesNotContain("padding-right: 258px", header.Groups["body"].Value);

            var meta = Regex.Match(template, @"\n\s*\.meta \{(?<body>[^}]*)\}");
            Assert.True(meta.Success, "the reference card rule is gone");
            Assert.DoesNotContain("position: absolute", meta.Groups["body"].Value);
        }

        [Fact]
        public void TheContractsTermChipIsNotCutInHalf()
        {
            var chips = Regex.Match(Template(), @"\.chip-row \{(?<body>[^}]*)\}");
            Assert.True(chips.Success);

            // Held on one line, the third chip — the contract's term — ran off
            // the end of the column it sits in.
            Assert.DoesNotContain("nowrap", chips.Groups["body"].Value);
            Assert.Contains("flex-wrap: wrap", chips.Groups["body"].Value);
        }

        [Fact]
        public void TheDocumentIsSetInAFaceEveryMachineThatRendersItHas()
        {
            var stack = Regex.Match(Template(), @"font-family:\s*(?<stack>[^;]*);");
            Assert.True(stack.Success);

            // "Inter" was first and is loaded nowhere, so the document fell
            // through to Tahoma on the customer's machine and to whatever the
            // print container happens to have.
            Assert.DoesNotContain("Inter", stack.Groups["stack"].Value);
            Assert.Contains("DejaVu Sans", stack.Groups["stack"].Value);
        }

        // ================================================== the consent line

        [Fact]
        public void TheContractLinkOpensTheContractRatherThanThePrivacyPolicy()
        {
            var root = RepositoryRoot();
            Assert.NotNull(root);

            var page = File.ReadAllText(Path.Combine(
                root!.FullName, "WitcherHub", "Pages", "Contracts", "Sign.cshtml"));

            // The link labelled "Vertrag" used to open the Datenschutzerklärung.
            var contractLink = Regex.Match(page, @"id=""contractLink""[^>]*");
            Assert.True(contractLink.Success, "the consent line no longer has a contract link");

            Assert.DoesNotContain("datenschutzerklaerung", contractLink.Value);
            Assert.Contains("ContractTermsAnchor", contractLink.Value);

            // The AGB link is a real external document and stays external.
            Assert.Contains("agb-fuer-agenturen", page);
        }
    }
}

using Ganss.Xss;
using Markdig;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The generated contract is set as a German contract.
    ///
    /// Reported plainly: "I wanted it as a German contract style. because so is
    /// not a German contact style, What I saw now No." It wasn't. The model was
    /// asked for the whole document and produced a web article — a title like a
    /// page heading, clauses numbered "1." "2." "3.", no party block and no
    /// signature block, so the thing could not be printed and signed as it
    /// stood.
    ///
    /// A German contract names the parties first — "zwischen … und …", each with
    /// the role it is called by throughout — states its terms as §§ with
    /// numbered (1) paragraphs, and closes with Ort, Datum and signature lines.
    /// None of that is wording, so none of it is left to the model; it is
    /// composed from the record the same way every time.
    ///
    /// The last test here is the one that would otherwise fail silently: the
    /// composed markup passes through Markdig and an HTML sanitiser before
    /// anyone sees it, and a sanitiser that dropped the class attributes would
    /// leave the document unstyled with every test above still green.
    /// </summary>
    public class GermanContractStyleTests
    {
        private static readonly GermanContractDocument.Parties Parties = new(
            ProviderName: "Netwitcher UG (haftungsbeschränkt)",
            ProviderAddress: "Kochhannstraße 6\n10249 Berlin\nDeutschland",
            CustomerName: "LS harbring",
            CustomerAddress: "Lorbeerplatz 28\n48085 Münster");

        private const string Clauses = """
            ## § 1 Gegenstand des Vertrags

            (1) Der Auftragnehmer erbringt die vereinbarten Leistungen.

            ## § 2 Vergütung und Zahlung

            (1) Die monatliche Pauschale beträgt 2.380,00 EUR.
            """;

        private static string Compose() => GermanContractDocument.Compose(
            title: "Dienstleistungsvertrag",
            contractNo: "C-2026-000001",
            projectTitle: "Online Verkauf",
            parties: Parties,
            clauses: Clauses,
            start: new DateOnly(2026, 8, 1),
            end: new DateOnly(2027, 3, 31));

        // =============================================================== form

        [Fact]
        public void TheDocumentOpensWithItsTypeAndReference()
        {
            var doc = Compose();

            // A German contract names its type on the first line. The project
            // name in that position reads as a web page.
            Assert.StartsWith("# Dienstleistungsvertrag", doc);

            Assert.Contains("Vertragsnummer: C-2026-000001", doc);
            Assert.Contains("Projekt: Online Verkauf", doc);
        }

        [Fact]
        public void ThePartiesAreNamedInTheGermanForm()
        {
            var doc = Compose();

            Assert.Contains("zwischen", doc);
            Assert.Contains("und", doc);

            // Each party followed by the name it is called by for the rest of
            // the document — without which every later clause reference to "der
            // Auftragnehmer" is unanchored.
            Assert.Contains("nachfolgend „Auftragnehmer“ genannt", doc);
            Assert.Contains("nachfolgend „Auftraggeber“ genannt", doc);

            Assert.Contains("Netwitcher UG", doc);
            Assert.Contains("LS harbring", doc);

            // The address is not reflowed into a sentence.
            Assert.Contains("Kochhannstraße 6<br />", doc);
            Assert.Contains("10249 Berlin<br />", doc);
        }

        [Fact]
        public void TheTermIsStatedInGermanDateFormat()
        {
            Assert.Contains("Laufzeit: 01.08.2026 bis 31.03.2027", Compose());
        }

        [Theory]
        [InlineData("2026-08-01", null, "ab 01.08.2026, auf unbestimmte Zeit")]
        [InlineData("2026-08-01", "2027-03-31", "01.08.2026 bis 31.03.2027")]
        [InlineData(null, "2027-03-31", "bis 31.03.2027")]
        public void AnOpenEndedTermSaysSoRatherThanLookingIncomplete(
            string? start, string? end, string expected)
        {
            var text = GermanContractDocument.Laufzeit(
                start is null ? null : DateOnly.Parse(start),
                end is null ? null : DateOnly.Parse(end));

            Assert.Contains(expected, text);
        }

        [Fact]
        public void AContractWithNoDatesStatesNoTerm()
        {
            // Inventing one would change what the contract says.
            Assert.Equal("", GermanContractDocument.Laufzeit(null, null));
        }

        [Fact]
        public void TheClausesArePassedThroughUnchanged()
        {
            var doc = Compose();

            Assert.Contains("## § 1 Gegenstand des Vertrags", doc);
            Assert.Contains("## § 2 Vergütung und Zahlung", doc);
            Assert.Contains("(1) Die monatliche Pauschale beträgt 2.380,00 EUR.", doc);
        }

        [Fact]
        public void TheDocumentCanBeSigned()
        {
            var doc = Compose();

            // The model produced no version of this at all, which is why the
            // printed document had nowhere to sign.
            Assert.Contains("Ort, Datum", doc);
            Assert.Contains("vertrag-unterschrift-linie", doc);

            // One line each, labelled with the role.
            Assert.Equal(2, Occurrences(doc, "vertrag-unterschrift-linie"));
        }

        [Fact]
        public void NoLegalClauseIsInvented()
        {
            var doc = Compose();

            // Writing these is not a formatting decision, and they are not ours
            // to add to somebody's contract.
            foreach (var clause in new[] { "Haftung", "Gerichtsstand", "Schlussbestimmungen", "Datenschutz" })
                Assert.DoesNotContain(clause, doc);
        }

        [Fact]
        public void CustomerTextCannotBreakTheDocument()
        {
            // These come from the customer record, where somebody may well have
            // typed a < or an &.
            var doc = GermanContractDocument.Compose(
                title: "Dienstleistungsvertrag",
                contractNo: "C-1",
                projectTitle: "A & B <script>",
                parties: new GermanContractDocument.Parties(
                    "Provider & Co", null, "<b>Customer</b>", null),
                clauses: "## § 1 Test");

            Assert.DoesNotContain("<script>", doc);
            Assert.DoesNotContain("<b>Customer", doc);
            Assert.Contains("&amp;", doc);
        }

        // ======================================================== the prompt

        [Fact]
        public void TheModelIsAskedForGermanClauseFormAndNothingElse()
        {
            var service = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "Contracts",
                "ContractDraftService.cs"));

            // § headings with (1) paragraphs, which is what makes it read as a
            // contract rather than as an article.
            Assert.Contains("## § 1 Gegenstand des Vertrags", service);
            Assert.Contains("(1) \", \"(2) \"", service.Replace("\"(1) \", \"(2) \"", "\"(1) \", \"(2) \""));

            // And explicitly not the frame, which is composed from the record.
            Assert.Contains("Do NOT write the document title", service);
        }

        [Fact]
        public void AFrameTheModelWroteAnywayIsNotShownTwice()
        {
            // The prompt says not to. A document with two titles is the sort of
            // thing a customer notices, so a stray one is dropped.
            var withFrame = """
                # Dienstleistungsvertrag

                zwischen Netwitcher UG und LS harbring

                ## § 1 Gegenstand des Vertrags

                (1) Leistung.

                Ort, Datum ____________________
                """;

            var stripped = ContractDraftService.StripComposedParts(withFrame);

            Assert.StartsWith("## § 1", stripped);
            Assert.DoesNotContain("# Dienstleistungsvertrag", stripped);
            Assert.DoesNotContain("Ort, Datum", stripped);
            Assert.Contains("(1) Leistung.", stripped);
        }

        [Fact]
        public void ClausesWithoutAnyFrameSurviveUntouched()
        {
            var stripped = ContractDraftService.StripComposedParts(Clauses);

            Assert.StartsWith("## § 1", stripped);
            Assert.Contains("2.380,00 EUR", stripped);
        }

        [Fact]
        public void AnAnswerWithNoSectionHeadingsIsKeptRatherThanEmptied()
        {
            // If the model ignored the form entirely, showing what it wrote beats
            // showing an empty contract.
            var prose = "Der Auftragnehmer erbringt die vereinbarten Leistungen.";

            Assert.Contains("Auftragnehmer", ContractDraftService.StripComposedParts(prose));
        }

        // ================================================= the rendered page

        [Fact]
        public void TheStylingSurvivesMarkdownAndTheSanitiser()
        {
            // The one that would otherwise fail silently: everything above can
            // pass while the sanitiser strips the class attributes and leaves the
            // document completely unstyled. Which is exactly what happened —
            // HtmlSanitizer does not allow the class attribute by default, and it
            // took this test to notice.
            //
            // Goes through the real renderer, not a copy of it. Testing a
            // reconstruction of the pipeline is how the five drifting copies of
            // it went unnoticed in the first place.
            var safe = WitcherHub.Rendering.ContractMarkdown.ToHtml(Compose());

            foreach (var hook in new[]
                     {
                         "vertrag-parteien",
                         "vertrag-partei",
                         "vertrag-rolle",
                         "vertrag-konjunktion",
                         "vertrag-unterschriften",
                         "vertrag-unterschrift-linie"
                     })
            {
                Assert.Contains(hook, safe);
            }

            // And the content is still there.
            Assert.Contains("Auftragnehmer", safe);
            Assert.Contains("2.380,00 EUR", safe);
            Assert.DoesNotContain("<script", safe);
        }

        [Fact]
        public void TheStylesheetSetsTheDocumentAsAGermanContract()
        {
            var css = File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "wwwroot", "css", "contracts", "contract-sign.css"));

            // A serif face, because that is what these documents are set in.
            Assert.Contains("Georgia", css);

            // Blocksatz with hyphenation — ragged-right reads as a draft, and
            // justification without hyphenation opens rivers in German compounds.
            Assert.Contains("text-align: justify", css);
            Assert.Contains("hyphens: auto", css);

            // A4 with the margins a Vertrag is set to.
            Assert.Contains("210mm", css);
            Assert.Contains("size: A4", css);

            // A § heading must not be the last line on a page.
            Assert.Contains("page-break-after: avoid", css);

            // And a signature block split across two pages is not signable.
            Assert.Contains("vertrag-unterschriften", css);
            Assert.Contains("page-break-inside: avoid", css);
        }

        // ---------------------------------------------------------------

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
    }
}

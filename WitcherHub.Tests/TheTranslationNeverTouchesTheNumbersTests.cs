using WitcherHub.Application.Services.Contracts.Language;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What the reading-aid translator is not allowed to see.
    ///
    /// A contract is mostly prose, but it also carries a price table, section
    /// numbers and markup, and a translator handed the whole document will
    /// eventually renumber a clause, write 1.900,00 as 1,900.00 because that is
    /// how the number reads in the target language, or return a table with a
    /// column missing. None of those is something the translation was asked to
    /// do; each changes what the contract says.
    ///
    /// So the structure is handled in code and the model only ever receives runs
    /// of words. These are about exactly which words those are — and, more to the
    /// point, which are held back.
    /// </summary>
    public class TheTranslationNeverTouchesTheNumbersTests
    {
        private const string Contract = """
            # Agenturvertrag

            **Vertragsnummer:** C-4711
            **Vertragsdatum:** 12.09.2026

            ## Vertragspartner

            **Anbieter**

            Netwitcher UG (haftungsbeschränkt)

            ## 1. Leistungsbeschreibung

            ### 1.1 Monatliche Betreuung

            **Leistungsumfang**

            Laufende Betreuung der Vertriebskanäle des Auftraggebers.

            **Nicht geschuldete Leistungen**

            - Mediabudget
            - Umsetzung technischer Änderungen

            ## 2. Vergütung

            | Pos. | Bezeichnung | Netto |
            |---|---|---:|
            | 1 | Monatliche Betreuung | 1.900,00 EUR |
            | | **Zwischensumme (Netto)** | **1.900,00 EUR** |
            | | Umsatzsteuer | 361,00 EUR |
            | | **Gesamtbetrag (Brutto)** | **2.261,00 EUR** |

            ## 3. Zahlungsverzug

            (1) Rechnungen sind innerhalb von 14 Tagen ab Zugang zur Zahlung fällig.

            (2) Die Umsetzung technischer Änderungen ist nicht Bestandteil der Leistungen.

            ## Unterschriften

            ________________________
            """;

        private static IReadOnlyList<MarkdownSegment> Segments() => TranslatableMarkdown.Split(Contract);

        // ============================================== what is never sent

        [Fact]
        public void NoFigureIsEverSentToBeTranslated()
        {
            // The rule the whole design turns on. A table cell holding a digit is
            // not sent at all, so no translation can reformat an amount — it is
            // safe by construction rather than by the model being careful.
            foreach (var segment in Segments())
            {
                Assert.DoesNotContain("1.900,00", segment.Text);
                Assert.DoesNotContain("361,00", segment.Text);
                Assert.DoesNotContain("2.261,00", segment.Text);
            }
        }

        [Fact]
        public void SectionNumbersStayBehindInThePrefix()
        {
            var heading = Assert.Single(Segments(), s => s.Text == "Leistungsbeschreibung");

            // The model receives "Leistungsbeschreibung" and cannot renumber it,
            // because it never sees the number.
            Assert.Equal("## 1. ", heading.Prefix);

            var sub = Assert.Single(Segments(), s => s.Text == "Monatliche Betreuung" && s.Cell < 0);
            Assert.Equal("### 1.1 ", sub.Prefix);
        }

        [Fact]
        public void TheTableSeparatorAndSignatureRuleAreNotSent()
        {
            var texts = Segments().Select(s => s.Text).ToList();

            Assert.DoesNotContain(texts, t => t.Contains("---", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, t => t.Contains("____", StringComparison.Ordinal));
        }

        [Fact]
        public void ContractNumbersAndDatesAreNotSentAsProse()
        {
            foreach (var segment in Segments())
            {
                Assert.DoesNotContain("C-4711", segment.Text);
                Assert.DoesNotContain("12.09.2026", segment.Text);
            }
        }

        // ================================================== what is sent

        [Fact]
        public void TheProseIsSent()
        {
            var texts = Segments().Select(s => s.Text).ToList();

            Assert.Contains("Laufende Betreuung der Vertriebskanäle des Auftraggebers.", texts);
            Assert.Contains("Mediabudget", texts);
            Assert.Contains("Umsetzung technischer Änderungen", texts);
        }

        [Fact]
        public void TableLabelsWithoutFiguresAreSent()
        {
            var texts = Segments().Select(s => s.Text).ToList();

            // "Zwischensumme (Netto)" is a label, not a figure, and a customer
            // reading in English needs it.
            Assert.Contains("Bezeichnung", texts);
            Assert.Contains("Zwischensumme (Netto)", texts);
            Assert.Contains("Umsatzsteuer", texts);
        }

        [Fact]
        public void ListMarkersAndParagraphNumbersStayBehind()
        {
            var item = Assert.Single(Segments(), s => s.Text == "Mediabudget");
            Assert.Equal("- ", item.Prefix);

            var paragraph = Assert.Single(
                Segments(), s => s.Text.StartsWith("Rechnungen sind", StringComparison.Ordinal));

            Assert.Equal("(1) ", paragraph.Prefix);
        }

        [Fact]
        public void BoldLabelsKeepTheirMarkers()
        {
            var label = Assert.Single(Segments(), s => s.Text == "Leistungsumfang");

            Assert.Equal("**", label.Prefix);
            Assert.Equal("**", label.Suffix);
        }

        // ================================================ putting it back

        [Fact]
        public void ATranslatedDocumentKeepsEveryFigureAndEveryNumber()
        {
            var segments = Segments();

            // Stands in for a translation: every segment comes back in capitals,
            // which is wrong German and right for proving the structure survives.
            var translations = segments.ToDictionary(
                s => s.FieldId, s => s.Text.ToUpperInvariant(), StringComparer.Ordinal);

            var result = TranslatableMarkdown.Reassemble(Contract, segments, translations);

            // Every figure, exactly as it was.
            Assert.Contains("| 1 | MONATLICHE BETREUUNG | 1.900,00 EUR |", result);
            Assert.Contains("361,00 EUR", result);
            Assert.Contains("2.261,00 EUR", result);

            // Every section number, exactly as it was.
            Assert.Contains("## 1. LEISTUNGSBESCHREIBUNG", result);
            Assert.Contains("### 1.1 MONATLICHE BETREUUNG", result);
            Assert.Contains("## 2. VERGÜTUNG", result);

            // Markers, bullets and paragraph numbers.
            Assert.Contains("- MEDIABUDGET", result);
            Assert.Contains("(1) RECHNUNGEN SIND", result);
            Assert.Contains("**LEISTUNGSUMFANG**", result);

            // The table shape and the signature rule.
            Assert.Contains("|---|---|---:|", result);
            Assert.Contains("________________________", result);
        }

        [Fact]
        public void ASegmentWithNoTranslationKeepsItsOriginalWords()
        {
            var segments = Segments();

            var result = TranslatableMarkdown.Reassemble(
                Contract, segments, new Dictionary<string, string>(StringComparer.Ordinal));

            // Nothing translated, nothing lost. The caller refuses partial
            // answers anyway, but a hole in a contract is not an acceptable
            // failure mode at any level.
            Assert.Equal(Contract.Replace("\r\n", "\n"), result);
        }

        [Fact]
        public void ReassemblingChangesNothingElseAboutTheDocument()
        {
            var segments = Segments();

            var translations = segments.ToDictionary(
                s => s.FieldId, s => s.Text, StringComparer.Ordinal);

            Assert.Equal(
                Contract.Replace("\r\n", "\n"),
                TranslatableMarkdown.Reassemble(Contract, segments, translations));
        }

        // ================================================== the cache key

        [Fact]
        public void TheFingerprintChangesWithTheWording()
        {
            var original = TranslatableMarkdown.Fingerprint(Contract);

            Assert.Equal(original, TranslatableMarkdown.Fingerprint(Contract));
            Assert.Equal(original, TranslatableMarkdown.Fingerprint(Contract.Replace("\n", "\r\n")));

            // A contract whose wording changed cannot be served a translation of
            // the wording it used to have.
            Assert.NotEqual(original, TranslatableMarkdown.Fingerprint(Contract + "\n\n## 4. Neu"));
        }

        [Fact]
        public void AnEmptyDocumentHasNothingToTranslate() =>
            Assert.Empty(TranslatableMarkdown.Split("   "));
    }
}

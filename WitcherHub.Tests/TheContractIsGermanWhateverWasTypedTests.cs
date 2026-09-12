using WitcherHub.Application.Services.Contracts;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The contract is German, whatever language the service was entered in.
    ///
    /// Services get typed in by whoever sells them, in whatever language they
    /// speak. The contract that comes out is a German B2B agreement, so the model
    /// is translating — and a translation nobody checked is a promise nobody
    /// checked. A scope that still reads "Monthly Social Media Management" or a
    /// line of Arabic left in the deliverables is not untidy: it is wording a
    /// customer may be asked to sign without being able to read it.
    ///
    /// The check has two halves and they are not equally certain, which is the
    /// point of testing them separately:
    ///
    ///   * A non-Latin script in a German contract's descriptive text is a fact.
    ///   * English is a judgement, made on function words — the, and, of, with,
    ///     shall — because those are the hardest part of English to leave out and
    ///     the least likely to appear in German. It is deliberately conservative.
    ///
    /// And a name is not a translation failure. An Arabic customer name, an
    /// Arabic signatory, a company registered in Arabic script are exactly the
    /// things that must not be translated. A check that refuses to issue a
    /// contract because of how somebody's name is spelled is worse than the
    /// problem it was written for.
    /// </summary>
    public class TheContractIsGermanWhateverWasTypedTests
    {
        private static IReadOnlyList<UntranslatedPassage> Inspect(
            string field, string? text, params string[] preserved) =>
            GermanOnlyCheck.Inspect([new(field, text)], preserved);

        // ============================================================ Arabic input

        [Fact]
        public void AnArabicServiceDescriptionLeftUntranslatedIsCaught()
        {
            var found = Inspect(
                "Leistungsbeschreibung 1 – Leistungsumfang",
                "خدمة مراقبة وتحسين ظهور الموقع في نتائج البحث");

            var passage = Assert.Single(found);

            Assert.Equal("Arabischer Text", passage.Reason);
            Assert.Equal("Leistungsbeschreibung 1 – Leistungsumfang", passage.Field);

            // The reviewer is shown what to fix, not merely told that something
            // is wrong somewhere.
            Assert.Contains("خدمة", passage.Text);
        }

        [Fact]
        public void TheSameContentTranslatedPassesCleanly() =>
            Assert.Empty(Inspect(
                "Leistungsbeschreibung 1 – Leistungsumfang",
                "Überwachung und Optimierung der organischen Sichtbarkeit der Website"));

        // =========================================================== English input

        [Fact]
        public void AnEnglishServiceDescriptionLeftUntranslatedIsCaught()
        {
            var found = Inspect(
                "Leistungsbeschreibung 1 – Leistungsumfang",
                "Monthly management of the client's social channels, including the " +
                "production and scheduling of content for Instagram.");

            Assert.Single(found);
            Assert.Contains("nglischer Text", found[0].Reason);
        }

        [Fact]
        public void TheSameContentInGermanPassesCleanly() =>
            Assert.Empty(Inspect(
                "Leistungsbeschreibung 1 – Leistungsumfang",
                "Monatliche Social-Media-Betreuung für Instagram, einschließlich Erstellung " +
                "und Planung der Inhalte."));

        [Fact]
        public void GermanKeepsItsBorrowedEnglishNouns()
        {
            // German contracts say Content, Reporting, Social Media and Monitoring
            // without being English. A rule built on nouns would fail all of these;
            // one built on function words does not.
            foreach (var german in new[]
                     {
                         "Monatliches Reporting der Sichtbarkeit im Social Media Monitoring.",
                         "Erstellung von Content für die Kanäle des Kunden.",
                         "Betreuung des Google Ads Accounts und laufendes Bid Management."
                     })
            {
                Assert.Empty(Inspect("Leistungsumfang", german));
            }
        }

        [Fact]
        public void MixedArabicAndEnglishIsCaughtOnce()
        {
            var found = Inspect(
                "Leistungsbeschreibung 1 – Liefergegenstände [1]",
                "تقرير شهري and a monthly summary of the results");

            // One passage, one finding — the reviewer is given the field to fix
            // rather than a finding per script.
            var passage = Assert.Single(found);
            Assert.Equal("Arabischer Text", passage.Reason);
        }

        // ======================================================== names are not text

        [Fact]
        public void AnArabicCompanyNameDoesNotFailAGermanSentence()
        {
            // The case that decides whether this check is usable at all. The
            // customer's company is registered in Arabic script; the contract is
            // in German; the name must appear exactly as it is.
            const string name = "شركة الأمل للتجارة";

            Assert.Empty(Inspect(
                "Leistungsbeschreibung 1 – Leistungsumfang",
                $"Laufende Betreuung der Vertriebskanäle der {name} im vereinbarten Umfang.",
                name));
        }

        [Fact]
        public void AnArabicPersonalNameDoesNotFailEither()
        {
            const string signer = "أحمد المنصوري";

            Assert.Empty(Inspect(
                "Leistungsbeschreibung 1 – Mitwirkungspflichten [1]",
                $"Ansprechpartner auf Kundenseite ist {signer}.",
                signer));
        }

        [Fact]
        public void APreservedNameIsMatchedEvenWhenTheSentenceInflectsAroundIt()
        {
            // Names reach a contract split across a line or with a German case
            // ending attached to the sentence around them, not always whole.
            Assert.Empty(Inspect(
                "Leistungsumfang",
                "Die Betreuung erfolgt für شركة الأمل im vereinbarten Umfang.",
                "شركة الأمل للتجارة"));
        }

        [Fact]
        public void BrandsUrlsAndAddressesAreNotTranslationFailures()
        {
            Assert.Empty(Inspect(
                "Leistungsumfang",
                "Veröffentlichung auf https://www.netwitcher.de und Versand an info@netwitcher.de. " +
                "Betreuung des Instagram-Kanals.",
                "Instagram"));
        }

        [Fact]
        public void AContractNumberIsNeverReadAsProse() =>
            Assert.Empty(Inspect(
                "Leistungsumfang",
                "Die Leistungen werden unter der Vertragsnummer C-4711 abgerechnet.",
                "C-4711"));

        // ============================================================ what is shown

        [Fact]
        public void TheFindingNamesTheFieldAndQuotesTheText()
        {
            var found = Inspect("Leistungsbeschreibung 2 – Annahmen [1]", "الوصول إلى الحسابات");

            var described = GermanOnlyCheck.Describe(found);

            Assert.Contains("Leistungsbeschreibung 2 – Annahmen [1]", described);
            Assert.Contains("Arabischer Text", described);
        }

        [Fact]
        public void NothingIsSilentlyDeleted()
        {
            // The untranslated text is carried in the finding, not removed from
            // the field. A check that tidied it away would leave a contract with
            // a hole in the scope and nothing to say what used to be there.
            var found = Inspect("Leistungsumfang", "تقرير شهري");

            Assert.Equal("تقرير شهري", found[0].Text);
        }

        [Fact]
        public void EmptyFieldsAreNotFindings() =>
            Assert.Empty(GermanOnlyCheck.Inspect(
                [new("Leistungsumfang", null), new("Annahmen", "   ")]));

        // ================================================================= money

        [Fact]
        public void MoneyIsGermanAndDoesNotBreakAcrossALine()
        {
            var formatted = GermanOnlyCheck.Money(1900m, "EUR");

            Assert.Equal("1.900,00 EUR", formatted);

            // A non-breaking space, so "1.900,00" never ends a line with "EUR"
            // starting the next one.
            Assert.DoesNotContain(' ', formatted);
        }
    }
}

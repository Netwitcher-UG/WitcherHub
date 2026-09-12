using WitcherHub.Application.Services.Contracts;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The paragraph sign is gone, and nothing went with it.
    ///
    /// The owner asked for "§ 1 Vertragsgegenstand" to read "1. Vertragsgegenstand",
    /// and the symbol is the easy half of that. The half worth testing is what
    /// happens to the two things that shared the character:
    ///
    ///   * "gemäß § 4 dieses Vertrags" is a pointer at a section of this
    ///     agreement. Drop the sign and it says "gemäß 4 dieses Vertrags", which
    ///     points at nothing.
    ///
    ///   * "§§ 611 ff. BGB" is a statement about which law governs the work.
    ///     Drop it and the sentence still reads, and means something else.
    ///
    /// So one becomes "Ziffer 4" and the other becomes "Paragraphen 611 ff. BGB",
    /// and telling them apart is done on the text rather than on a guess: German
    /// statute abbreviations carry two or more capitals, and the German words that
    /// follow an internal reference carry one.
    /// </summary>
    public class AGermanContractWithoutTheSignTests
    {
        // ===================================================== internal references

        [Theory]
        [InlineData("gemäß § 4 dieses Vertrags", "gemäß Ziffer 4 dieses Vertrags")]
        [InlineData("nach § 2 dieser Vereinbarung", "nach Ziffer 2 dieser Vereinbarung")]
        [InlineData("siehe §§ 3 und 4", "siehe Ziffern 3 und 4")]
        [InlineData("§4 gilt entsprechend", "Ziffer 4 gilt entsprechend")]
        public void AReferenceToThisContractBecomesAZiffer(string source, string expected) =>
            Assert.Equal(expected, GermanLegalText.SpellOutParagraphSigns(source));

        // ==================================================== statutory references

        [Theory]
        [InlineData("§ 126a BGB", "Paragraph 126a BGB")]
        [InlineData("§§ 611 ff. BGB", "Paragraphen 611 ff. BGB")]
        [InlineData("§ 640 Abs. 2 BGB", "Paragraph 640 Abs. 2 BGB")]
        [InlineData("§ 305b BGB angreifbar", "Paragraph 305b BGB angreifbar")]
        [InlineData("Art. 28 DSGVO und § 11 BDSG", "Art. 28 DSGVO und Paragraph 11 BDSG")]
        public void AReferenceToAStatuteKeepsItsMeaningAndLosesOnlyTheSymbol(
            string source, string expected) =>
            Assert.Equal(expected, GermanLegalText.SpellOutParagraphSigns(source));

        [Fact]
        public void AStatutoryReferenceIsNeverSilentlyDeleted()
        {
            var converted = GermanLegalText.SpellOutParagraphSigns(
                "Die Leistungen sind Dienstleistungen im Sinne der §§ 611 ff. BGB.");

            // The number, the range and the statute all survive. Removing the
            // symbol by deleting the citation would leave a sentence that reads
            // perfectly well and says something the parties did not agree.
            Assert.Contains("611", converted);
            Assert.Contains("ff.", converted);
            Assert.Contains("BGB", converted);
            Assert.Contains("Paragraphen", converted);
        }

        [Fact]
        public void TheSentenceBoundaryIsNotCrossedLookingForAStatute()
        {
            // The naive version of this reads ahead a fixed number of words and
            // finds "BGB" in the next sentence, turning an internal reference
            // into a statutory one.
            var converted = GermanLegalText.SpellOutParagraphSigns(
                "Maßgeblich ist § 4 dieses Vertrags. Im Übrigen gilt das BGB.");

            Assert.Contains("Ziffer 4 dieses Vertrags", converted);
            Assert.DoesNotContain("Paragraph 4", converted);
        }

        [Fact]
        public void ConvertingTwiceChangesNothingTheSecondTime()
        {
            const string source = "Nach § 4 dieses Vertrags und § 126a BGB.";

            var once = GermanLegalText.SpellOutParagraphSigns(source);

            Assert.Equal(once, GermanLegalText.SpellOutParagraphSigns(once));
        }

        // =============================================================== numbering

        [Fact]
        public void ASectionHeadingIsNumberedWithoutTheSign()
        {
            Assert.Equal("1. Vertragsgegenstand", GermanLegalText.Heading(1, "Vertragsgegenstand"));
            Assert.Equal("2. Leistungen des Anbieters", GermanLegalText.Heading(2, "Leistungen des Anbieters"));
            Assert.Equal("3. Vergütung", GermanLegalText.Heading(3, "Vergütung"));
        }

        [Fact]
        public void SubsectionsAreNumberedHierarchically()
        {
            Assert.Equal("1.1 Leistungsumfang", GermanLegalText.Heading(1, 1, "Leistungsumfang"));
            Assert.Equal("1.2 Liefergegenstände", GermanLegalText.Heading(1, 2, "Liefergegenstände"));
            Assert.Equal("1.3 Nicht enthalten", GermanLegalText.Heading(1, 3, "Nicht enthalten"));
        }

        [Theory]
        [InlineData("§ 1 Vertragsgegenstand")]
        [InlineData("§1 Vertragsgegenstand")]
        [InlineData("1. Vertragsgegenstand")]
        [InlineData("1 Vertragsgegenstand")]
        [InlineData("1.1 Vertragsgegenstand")]
        [InlineData("Ziffer 1 Vertragsgegenstand")]
        public void NumberingTheModelAddedAnywayIsNotDoubled(string heading)
        {
            // "1. § 1 Vertragsgegenstand" is the failure this prevents, and every
            // one of these is a shape a model has actually returned.
            Assert.Equal("1. Vertragsgegenstand", GermanLegalText.Heading(1, heading));
        }

        [Fact]
        public void AnEmptyHeadingGetsAnHonestPlaceholderRatherThanABlankLine()
        {
            Assert.Equal("7. Abschnitt 7", GermanLegalText.Heading(7, "   "));
            Assert.Equal("7. Abschnitt 7", GermanLegalText.Heading(7, "§ 7"));
        }

        // ============================================================== the sweep

        [Fact]
        public void ASignThatSurvivesIsReportedWithItsLineRatherThanRemoved()
        {
            var found = GermanLegalText.FindParagraphSigns(
                "## 1. Vertragsgegenstand\n\nEs gilt § 99 einer unbekannten Ordnung.\n");

            // Reported, not deleted. Something upstream produced text nobody
            // converted, and a contract is not the place to paper over that.
            Assert.Single(found);
            Assert.Contains("§ 99", found[0]);
        }

        [Fact]
        public void ACleanDocumentReportsNothing() =>
            Assert.Empty(GermanLegalText.FindParagraphSigns(
                "## 1. Vertragsgegenstand\n\nNach Ziffer 2 dieses Vertrags.\n"));
    }
}

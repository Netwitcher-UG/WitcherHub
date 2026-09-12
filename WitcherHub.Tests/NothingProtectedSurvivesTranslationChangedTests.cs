using WitcherHub.Application.Services.Contracts.Language;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The values a translator must not touch, and the proof that it did not.
    ///
    /// A model asked to put a sentence into German will, given the chance,
    /// render "Netwitcher UG (haftungsbeschränkt)" as "Netwitcher Ltd",
    /// helpfully translate "haftungsbeschränkt", write 1.900,00 as 1,900.00
    /// because that is how the number reads in the source language, or turn
    /// 03.08.2026 into 08/03/2026 for a reader who will take it for March. None
    /// of those is an error the model would call an error; each one changes what
    /// the contract says.
    ///
    /// So those values never reach it, and the proof is arithmetic rather than
    /// trust: every occurrence goes out as an opaque marker, and the markers are
    /// counted on the way back.
    /// </summary>
    public class NothingProtectedSurvivesTranslationChangedTests
    {
        // ================================================================ masking

        [Fact]
        public void AProtectedValueLeavesAsAMarkerAndComesBackAsItself()
        {
            var vault = new ProtectedValueVault();

            var token = vault.Protect("Netwitcher UG (haftungsbeschränkt)", ProtectedValueKind.LegalCompanyName);

            var masked = vault.Mask("Der Anbieter Netwitcher UG (haftungsbeschränkt) erbringt die Leistungen.");

            Assert.DoesNotContain("Netwitcher", masked);
            Assert.Contains(token, masked);

            Assert.Equal(
                "Der Anbieter Netwitcher UG (haftungsbeschränkt) erbringt die Leistungen.",
                vault.Restore(masked));
        }

        [Fact]
        public void TheSameValueTwiceIsOneMarkerRatherThanTwo()
        {
            var vault = new ProtectedValueVault();

            var first = vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);
            var again = vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);

            // A company name in four fields is one rule the model has to respect,
            // not four chances to get it wrong.
            Assert.Equal(first, again);
            Assert.Single(vault.Values);
        }

        [Fact]
        public void ALongerNameIsMaskedBeforeTheShorterOneInsideIt()
        {
            var vault = new ProtectedValueVault();

            vault.Protect("Netwitcher", ProtectedValueKind.Brand);
            var full = vault.Protect("Netwitcher UG (haftungsbeschränkt)", ProtectedValueKind.LegalCompanyName);

            var masked = vault.Mask("Vertrag mit Netwitcher UG (haftungsbeschränkt).");

            // Shortest-first would leave "{{PROTECTED_001}} UG (haftungsbeschränkt)"
            // — a marker glued to half a legal name, which restores to something
            // that is neither value.
            Assert.Contains(full, masked);
            Assert.Equal("Vertrag mit Netwitcher UG (haftungsbeschränkt).", vault.Restore(masked));
        }

        // ============================================================== verifying

        [Fact]
        public void AMarkerTheModelDroppedIsCaught()
        {
            var vault = new ProtectedValueVault();
            vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);

            var sent = vault.Mask("Leistungen für Musterfirma GmbH.");

            Assert.False(ProtectedValueVault.Matches(sent, "Leistungen für den Kunden.", out var why));
            Assert.Contains("fehlt", why);
        }

        [Fact]
        public void AMarkerTheModelDuplicatedIsCaught()
        {
            var vault = new ProtectedValueVault();
            var token = vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);

            var sent = vault.Mask("Leistungen für Musterfirma GmbH.");

            // Repeating it puts the customer's name in the contract twice.
            Assert.False(
                ProtectedValueVault.Matches(sent, $"Leistungen für {token} und {token}.", out var why));

            Assert.Contains("2×", why);
        }

        [Fact]
        public void AMarkerTheModelInventedIsCaught()
        {
            var vault = new ProtectedValueVault();
            vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);

            var sent = vault.Mask("Leistungen für Musterfirma GmbH.");

            Assert.False(ProtectedValueVault.Matches(
                sent, "Leistungen für {{PROTECTED_001}} und {{PROTECTED_009}}.", out var why));

            Assert.Contains("unbekannten Platzhalter", why);
        }

        [Fact]
        public void MarkersThatSurviveIntactPass()
        {
            var vault = new ProtectedValueVault();
            var token = vault.Protect("Musterfirma GmbH", ProtectedValueKind.LegalCompanyName);

            var sent = vault.Mask("Services for Musterfirma GmbH.");

            Assert.True(ProtectedValueVault.Matches(sent, $"Leistungen für {token}.", out _));
        }

        [Fact]
        public void AMarkerReachingADocumentIsDetectable()
        {
            // Internal machinery printed where a company name belongs is worse
            // than the translation it was protecting.
            Assert.True(ProtectedValueVault.ContainsToken("Vertrag mit {{PROTECTED_001}}."));
            Assert.False(ProtectedValueVault.ContainsToken("Vertrag mit Musterfirma GmbH."));
        }
    }
}

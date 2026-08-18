namespace WitcherHub.Tests
{
    /// <summary>
    /// Every position read out of a supplied contract can be ticked and added.
    ///
    /// Reported as "I cannot select any extracted positions. Selecting is not
    /// working here." Nothing was broken about the checkboxes — they were
    /// rendered disabled on purpose, for any charge whose amount the document
    /// did not settle.
    ///
    /// The intent was sound: adding one would mean inventing a quantity. The
    /// conclusion was not. A services contract is largely made of monthly fees,
    /// hourly rates and conditional costs, so on the owner's real document every
    /// single row came back disabled and the button could only ever answer "Tick
    /// at least one position to add." Refusing does not spare anyone the missing
    /// number; it just means retyping the title, description, rate, unit and
    /// frequency by hand before they can supply it.
    ///
    /// So the reading is offered in full and what it could not determine is said
    /// on the row. The guard that actually matters — nothing unpriced reaching a
    /// contract — is the position validator, which still refuses to save a line
    /// with no price and is not weakened here.
    ///
    /// These read the built file rather than run it: the behaviour lives in
    /// markup and a handler, and the point is that neither reintroduces the
    /// block.
    /// </summary>
    public class ExtractedPositionsAreSelectableTests
    {
        private static string Builder() => File.ReadAllText(Path.Combine(
            TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "positions-builder.js"));

        [Fact]
        public void NoExtractedPositionIsRenderedWithADisabledCheckbox()
        {
            var source = Builder();
            var render = Between(source, "function renderExtractedPositions", "function collectExtractionEdits");

            Assert.NotEqual("", render);

            // The exact shape of the old bug: a disabled tick for anything the
            // reading could not price.
            Assert.DoesNotContain("form-check-input\" disabled", render);
            Assert.DoesNotContain("checkbox\" disabled", render);
        }

        [Fact]
        public void EverythingTickedIsAdded()
        {
            var source = Builder();
            var handler = Between(source, "action === \"add-extracted-positions\"", "action === \"approve-version\"");

            Assert.NotEqual("", handler);

            // The old handler split the ticked rows into "addable" and "blocked"
            // and dropped the second group on the floor.
            Assert.DoesNotContain("const addable = chosen.filter", handler);
            Assert.DoesNotContain("if (addable.length === 0)", handler);

            // Every ticked row is walked.
            Assert.Contains("chosen.forEach", handler);
        }

        [Fact]
        public void WhatTheDocumentCouldNotPriceIsSaidRatherThanHidden()
        {
            var source = Builder();
            var handler = Between(source, "action === \"add-extracted-positions\"", "action === \"approve-version\"");

            // Counted and reported, so the user knows which lines need a figure
            // before the save will go through.
            Assert.Contains("needsFigures", handler);
            Assert.Contains("canBecomePosition === false", handler);

            // And carried onto the position, so it survives the reading
            // scrolling away and survives a reload.
            Assert.Contains("blockedReason", handler);
        }

        [Fact]
        public void TheRowSaysWhatIsMissingWithoutForbiddingIt()
        {
            var source = Builder();
            var render = Between(source, "function renderExtractedPositions", "function collectExtractionEdits");

            Assert.Contains("needsAttention", render);

            // Phrased as something the user can act on.
            Assert.Contains("still add it", render);
        }

        [Fact]
        public void ThePriceGuardIsStillInTheValidator()
        {
            // The reason it is safe to offer these: the server refuses to store a
            // position with no price behind it, so nothing unpriced reaches a
            // contract because a checkbox became tickable.
            var validator = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Application", "Validators", "Contract",
                "ManualPositionValidator.cs"));

            Assert.Contains("Enter a price, or mark the position as free.", validator);
            Assert.Contains("Quantity must be greater than zero.", validator);
        }

        // ---------------------------------------------------------------

        private static string Between(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return "";

            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            return end < 0 ? text[start..] : text[start..end];
        }
    }
}

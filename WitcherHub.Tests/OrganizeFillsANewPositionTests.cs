using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// Describing a position in prose fills its fields, price included.
    ///
    /// Reported as: the button makes new fields but does not populate them, and a
    /// price written in the text leaves the Price box empty.
    ///
    /// It did, by construction. The organiser was written for one job — tidy the
    /// positions you already entered, never touch their money — and the button is
    /// used for another: describe a position and have it filled in. For a position
    /// that had no counterpart in the list, Reconcile ran
    ///
    ///     candidate.UnitPrice = null;   // "the model does not get to price work"
    ///
    /// and the prompt forbade the model from setting quantity, price, cycle, VAT,
    /// discount or dates at all. So the only fields that could arrive populated
    /// were the descriptive ones, and everything commercial came back as the DTO's
    /// defaults.
    ///
    /// The rule was right and the case was wrong. It protects a figure the user
    /// typed into the form from being rewritten. A position that exists only
    /// because the user described it in prose has no such figure to protect — the
    /// prose is what they typed.
    ///
    /// These tests drive Reconcile directly with the shape a model returns, so
    /// they need no API key and assert behaviour rather than wording.
    /// </summary>
    public class OrganizeFillsANewPositionTests
    {
        private static OrganizePositionsRequest Request(params ManualPositionDto[] existing) => new()
        {
            RoughInput = "irrelevant here; the model's answer is supplied directly",
            ExistingPositions = existing.ToList(),
            Currency = "EUR"
        };

        /// <summary>A position as the model returns one it read out of the notes.</summary>
        private static ManualPositionDto Proposed(
            string title,
            decimal? unitPrice = null,
            decimal quantity = 1m,
            PricingModel pricing = PricingModel.Fixed,
            BillingCycle cycle = BillingCycle.OneTime,
            string? clientId = null) => new()
            {
                ClientId = clientId ?? "",
                Title = title,
                UnitPrice = unitPrice,
                Quantity = quantity,
                PricingModel = pricing,
                BillingCycle = cycle,
                Currency = "EUR"
            };

        // ======================================= a position only described

        [Fact]
        public void ThePriceInTheTextReachesThePriceField()
        {
            // The whole reported symptom, in one assertion.
            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(),
                [Proposed("Monatliche Betreuung", unitPrice: 2380m, cycle: BillingCycle.Monthly)]);

            var added = Assert.Single(positions);

            Assert.Equal(2380m, added.UnitPrice);
            Assert.Equal(BillingCycle.Monthly, added.BillingCycle);
        }

        [Fact]
        public void AQuantityAndAnHourlyRateBothSurvive()
        {
            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(),
                [Proposed("Schulung", unitPrice: 120m, quantity: 8m, pricing: PricingModel.Hourly)]);

            var added = Assert.Single(positions);

            Assert.Equal(120m, added.UnitPrice);
            Assert.Equal(8m, added.Quantity);
            Assert.Equal(PricingModel.Hourly, added.PricingModel);

            // And the line totals to the figure a reader would expect.
            Assert.Equal(960m, added.NetTotal);
        }

        [Fact]
        public void WorkDescribedWithNoPriceStillArrivesWithoutOne()
        {
            // Never invent one. An empty Price is correct when the notes gave none.
            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(), [Proposed("SEO-Monitoring")]);

            Assert.Null(Assert.Single(positions).UnitPrice);
        }

        [Fact]
        public void APriceOfZeroIsNotAPrice()
        {
            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(), [Proposed("Beratung", unitPrice: 0m)]);

            // Zero is what a model returns when it has nothing to say, and it would
            // read as agreed free work.
            Assert.Null(Assert.Single(positions).UnitPrice);
        }

        [Fact]
        public void AStatedPriceBeatsAFreeFlag()
        {
            var proposed = Proposed("Einrichtung", unitPrice: 900m);
            proposed.IsFree = true;

            var (positions, _, _) = AiPositionOrganizer.Reconcile(Request(), [proposed]);

            var added = Assert.Single(positions);

            Assert.False(added.IsFree);
            Assert.Equal(900m, added.UnitPrice);
        }

        [Fact]
        public void AMissingQuantityBecomesOneRatherThanZero()
        {
            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(), [Proposed("Pauschale", unitPrice: 500m, quantity: 0m)]);

            // A quantity of zero prices the line at nothing whatever the rate is.
            Assert.Equal(1m, Assert.Single(positions).Quantity);
        }

        [Fact]
        public void TheReviewSaysWhatWasReadOutOfTheNotes()
        {
            var (_, changes, _) = AiPositionOrganizer.Reconcile(
                Request(),
                [Proposed("Monatliche Betreuung", unitPrice: 2380m, cycle: BillingCycle.Monthly)]);

            var added = Assert.Single(changes, c => c.Kind == PositionChangeKind.AddedPosition);

            // German notation, because that is how the figure was written and how
            // it will be shown.
            Assert.Contains("2.380,00", added.After);
            Assert.Contains("Monthly", added.After);
        }

        [Fact]
        public void SomethingDescribedAsFreeStaysFree()
        {
            var proposed = Proposed("Ersteinrichtung");
            proposed.IsFree = true;

            var added = Assert.Single(AiPositionOrganizer.Reconcile(Request(), [proposed]).Positions);

            Assert.True(added.IsFree);
            Assert.Equal(0m, added.NetTotal);
        }

        // ================================ a position the user already entered

        [Fact]
        public void AnEnteredPositionKeepsItsOwnFigures()
        {
            // The rule that was always right, and still is: the user typed these.
            var mine = new ManualPositionDto
            {
                ClientId = "abc",
                Title = "Monatliche Betreuung",
                UnitPrice = 2380m,
                Quantity = 1m,
                BillingCycle = BillingCycle.Monthly,
                Currency = "EUR"
            };

            var theirs = Proposed("Monatliche Betreuung", unitPrice: 1900m,
                cycle: BillingCycle.Annual, clientId: "abc");

            var (positions, _, rejected) = AiPositionOrganizer.Reconcile(Request(mine), [theirs]);

            var kept = Assert.Single(positions);

            Assert.Equal(2380m, kept.UnitPrice);
            Assert.Equal(BillingCycle.Monthly, kept.BillingCycle);

            // And the attempt is reported rather than swallowed.
            Assert.Contains(rejected, r => r.Field == "UnitPrice");
            Assert.Contains(rejected, r => r.Field == "BillingCycle");
        }

        [Fact]
        public void MatchingHappensOnTheTitleWhenNoIdComesBack()
        {
            // Models drop the id. Without the title fallback an existing position
            // would be treated as new and re-priced from the notes.
            var mine = new ManualPositionDto
            {
                ClientId = "abc",
                Title = "Monatliche Betreuung",
                UnitPrice = 2380m,
                Currency = "EUR"
            };

            var (positions, _, rejected) = AiPositionOrganizer.Reconcile(
                Request(mine), [Proposed("Monatliche Betreuung", unitPrice: 1m)]);

            Assert.Equal(2380m, Assert.Single(positions).UnitPrice);
            Assert.Contains(rejected, r => r.Field == "UnitPrice");
        }

        [Fact]
        public void AnEnteredPositionTheModelForgotIsNotLost()
        {
            var mine = new ManualPositionDto
            {
                ClientId = "abc", Title = "Bestehende Leistung", UnitPrice = 100m, Currency = "EUR"
            };

            var (positions, _, _) = AiPositionOrganizer.Reconcile(
                Request(mine), [Proposed("Etwas ganz anderes", unitPrice: 50m)]);

            Assert.Equal(2, positions.Count);
            Assert.Contains(positions, p => p.Title == "Bestehende Leistung" && p.UnitPrice == 100m);
        }

        // ============================================== what the model is told

        /// <summary>
        /// Prose in a source file is wrapped to fit the column, so a sentence that
        /// reads as one line is several. Compare with the wrapping collapsed.
        /// </summary>
        private static string Unwrapped(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        [Fact]
        public void ThePromptSeparatesTheTwoCases()
        {
            var source = Unwrapped(File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "OpenAI",
                "AiPositionOrganizer.cs")));

            // The blanket ban applied to new positions too, so the model was told
            // not to set a price on work that had none — which is most of why the
            // fields came back empty.
            Assert.Contains("A position that is ALREADY ENTERED", source);
            Assert.Contains("A NEW position", source);
            Assert.Contains("read the figures out of them", source);

            // German notation is the point of the whole exercise.
            Assert.Contains("a full stop groups thousands and a comma is the decimal", source);

            // And the boundary stays: nothing may be made up.
            Assert.Contains("Leave a figure null when the notes do not give it", source);
            Assert.Contains("never copy a figure from another position onto a new one", source);
        }

        [Fact]
        public void TheScreenNoLongerPromisesToIgnoreThePrice()
        {
            var page = Unwrapped(File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "Pages", "Contracts", "Positions.cshtml")));

            // The hint said prices "stay exactly as you entered them", which reads
            // as a promise that a price typed into this box will be ignored.
            Assert.DoesNotContain(
                "Prices, quantities, VAT, discounts, dates and billing cycles stay exactly as you", page);

            Assert.Contains("it will be filled in for you", page);
            Assert.Contains("Positions already in the list keep the", page);

            var script = Unwrapped(File.ReadAllText(Path.Combine(
                TestPaths.WebProject, "wwwroot", "js", "pages", "contracts", "positions-builder.js")));

            // The markup, not the prose: the comment above that line explains what
            // it used to say, and asserting against the explanation is how a test
            // comes to fail on its own documentation.
            Assert.DoesNotContain("<span class=\"text-secondary\">(no price", script);
        }
    }
}

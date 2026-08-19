using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// A generated contract accounts for everything that was agreed.
    ///
    /// Reported as "the contract is far too short — it fits on one page". It did,
    /// and it always did: the prompt named seven headings and asked for those,
    /// so three positions and thirty positions produced the same seven short
    /// clauses. Nothing anywhere compared the document to the record, so a run
    /// that dropped most of the agreed scope looked exactly like a run that had
    /// little to say.
    ///
    /// The fix is not a longer prompt. Everything the contract must account for is
    /// enumerated first, each entry with an internal id; the plan assigns every id
    /// to a section; and the finished text is measured against the ledger before
    /// it is saved. Length is the output of that, never a target.
    ///
    /// Everything below is built from synthetic data. No customer, service or
    /// price from the live system appears here — a test that pins today's contract
    /// stops being a test the day the contract changes.
    /// </summary>
    public class ContractCoverageTests
    {
        // ======================================================== the ledger

        [Fact]
        public void EveryPartOfAPositionBecomesSomethingTheContractMustCover()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            // The parts a position carries beyond its name and price. Each one is
            // its own entry so that the audit can say which was dropped, rather
            // than reporting "position 1" as covered because its title appears.
            foreach (var part in new[]
                     {
                         "Umfang", "Liefergegenstände", "Abnahmekriterien",
                         "Mitwirkung des Auftraggebers", "Annahmen", "Ausschlüsse",
                         "Hinweise", "Vergütung"
                     })
            {
                Assert.Contains(ledger.Items, i => i.Topic.Contains(part, StringComparison.Ordinal));
            }
        }

        [Fact]
        public void MorePositionsMeanMoreToCover()
        {
            // The property the seven-heading prompt did not have: what has to be
            // written grows with what was agreed.
            var few = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            var many = ContractCoverageLedger.FromRecord(
                Context(Enumerable.Range(1, 10).Select(APosition).ToArray()));

            Assert.True(many.Count > few.Count * 5,
                $"ten positions produced {many.Count} entries against {few.Count} for one");
        }

        [Fact]
        public void APositionWithNothingButANameStillHasToBeWrittenAbout()
        {
            var bare = new ManualPositionDto { Position = 1, Title = "Grundleistung", UnitPrice = 100m };

            var ledger = ContractCoverageLedger.FromRecord(Context(bare));

            // Two: what it is, and what it costs. Neither may be skipped because
            // the position is thin.
            Assert.Contains(ledger.Items, i => i.Evidence.Contains("Grundleistung"));
            Assert.Contains(ledger.Items, i => i.IsCommercial && i.Topic.Contains("Vergütung"));
        }

        [Fact]
        public void FiguresAreCarriedAsGermanTextThatMustAppearVerbatim()
        {
            var position = new ManualPositionDto
            {
                Position = 1,
                Title = "Leistung",
                PricingModel = PricingModel.Fixed,
                UnitPrice = 2380m,
                Currency = "EUR"
            };

            var money = ContractCoverageLedger.FromRecord(Context(position))
                .Items.Single(i => i.Topic.Contains("Vergütung"));

            // 2.380,00 — not 2380, not 2,380.00. A contract that states the price
            // in the wrong notation states a different price to a German reader.
            Assert.Contains("2.380,00", money.Evidence);
            Assert.True(money.IsCommercial);
        }

        [Fact]
        public void SomethingGivenAwayHasNoPriceToCheckAgainst()
        {
            var free = new ManualPositionDto
            {
                Position = 1,
                Title = "Einrichtung",
                IsFree = true
            };

            var money = ContractCoverageLedger.FromRecord(Context(free))
                .Items.Single(i => i.Topic.Contains("Vergütung"));

            // Requiring a figure here would report every free line as a missing
            // price for ever.
            Assert.Empty(money.Evidence);
            Assert.Contains("ohne Berechnung", money.Detail);
        }

        [Fact]
        public void TheBillingCycleIsStatedInTheContractsOwnLanguage()
        {
            var monthly = new ManualPositionDto
            {
                Position = 1,
                Title = "Betreuung",
                UnitPrice = 100m,
                BillingCycle = BillingCycle.Monthly
            };

            var money = ContractCoverageLedger.FromRecord(Context(monthly))
                .Items.Single(i => i.Topic.Contains("Vergütung"));

            // "Monthly" is the database's word. A German contract says monatlich.
            Assert.Contains("monatlich", money.Detail);
            Assert.DoesNotContain("Monthly", money.Detail);
        }

        [Fact]
        public void TopicsFoundInAPastedDocumentNeverCarryItsFigures()
        {
            var ledger = ContractCoverageLedger
                .FromRecord(Context(APosition(1)))
                .WithSourceTopics([new ContractCoverageLedger.SourceTopic("Wartung", "Monatliche Wartung vereinbart")]);

            var topic = ledger.Items.Single(i => i.Origin == CoverageOrigin.Source);

            // The record outranks a pasted document, so holding the new contract to
            // a figure out of somebody's old one would require it to contradict
            // itself.
            Assert.Empty(topic.Evidence);
            Assert.StartsWith("SRC-", topic.Id);
        }

        [Fact]
        public void EveryEntryHasAnIdOfItsOwn()
        {
            var ledger = ContractCoverageLedger.FromRecord(
                Context(Enumerable.Range(1, 6).Select(APosition).ToArray()));

            var ids = ledger.Items.Select(i => i.Id).ToList();

            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        // ========================================================= the audit

        [Fact]
        public void AnEntryNoSectionPlannedIsMissing()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));
            var orphan = ledger.Items[^1];

            var plan = ledger.Items
                .Where(i => i.Id != orphan.Id)
                .Select(i => new ContractCoverageAudit.PlannedSection("§", [i.Id], true))
                .ToList();

            var audit = ContractCoverageAudit.Measure(ledger, plan, Everything(ledger));

            Assert.Contains(audit.Gaps, g => g.Item.Id == orphan.Id && g.Reason == CoverageGapReason.NotPlanned);
        }

        [Fact]
        public void AnEntryWhoseSectionCameBackEmptyIsMissing()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            var plan = ledger.Items
                .Select(i => new ContractCoverageAudit.PlannedSection("§", [i.Id], HasContent: false))
                .ToList();

            var audit = ContractCoverageAudit.Measure(ledger, plan, "");

            Assert.All(audit.Gaps, g => Assert.Equal(CoverageGapReason.NotWritten, g.Reason));
            Assert.Equal(ledger.Count, audit.Gaps.Count);
        }

        [Fact]
        public void AFigureThatWasParaphrasedIsMissing()
        {
            var position = new ManualPositionDto
            {
                Position = 1,
                Title = "Leistung",
                PricingModel = PricingModel.Fixed,
                UnitPrice = 2380m
            };

            var ledger = ContractCoverageLedger.FromRecord(Context(position));

            var plan = ledger.Items
                .Select(i => new ContractCoverageAudit.PlannedSection("§", [i.Id], true))
                .ToList();

            // Reads perfectly well and states nothing.
            var audit = ContractCoverageAudit.Measure(
                ledger, plan,
                "Leistung. Die Vergütung richtet sich nach den vereinbarten Positionen.");

            var money = audit.Gaps.Single(g => g.Item.Topic.Contains("Vergütung"));

            Assert.Equal(CoverageGapReason.EvidenceMissing, money.Reason);

            // And it is the kind of gap that matters, not a stylistic thinness.
            Assert.True(money.IsCritical);
        }

        [Fact]
        public void AFigureIsRecognisedHoweverTheModelSpacedIt()
        {
            var position = new ManualPositionDto
            {
                Position = 1,
                Title = "Leistung",
                PricingModel = PricingModel.Fixed,
                UnitPrice = 2380m
            };

            var ledger = ContractCoverageLedger.FromRecord(Context(position));

            var plan = ledger.Items
                .Select(i => new ContractCoverageAudit.PlannedSection("§", [i.Id], true))
                .ToList();

            // A non-breaking space inside the figure, which is what a model writing
            // German typography produces. Matching literally would report a price
            // that is right there as missing.
            var audit = ContractCoverageAudit.Measure(
                ledger, plan, "Leistung. Die Vergütung beträgt 2.380,00 EUR.");

            Assert.DoesNotContain(audit.Gaps, g => g.Item.Topic.Contains("Vergütung"));
        }

        [Fact]
        public void ACompleteContractHasNothingToReport()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            var plan = ledger.Items
                .Select(i => new ContractCoverageAudit.PlannedSection("§", [i.Id], true))
                .ToList();

            var audit = ContractCoverageAudit.Measure(ledger, plan, Everything(ledger));

            Assert.True(audit.IsComplete, audit.Summary);
            Assert.Empty(audit.ReviewNotes());
            Assert.Equal(1d, audit.Ratio);
        }

        [Fact]
        public void WhatTheReviewerIsToldNeverMentionsAnInternalId()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            var audit = ContractCoverageAudit.Measure(
                ledger, Array.Empty<ContractCoverageAudit.PlannedSection>(), "");

            Assert.NotEmpty(audit.ReviewNotes());

            foreach (var note in audit.ReviewNotes())
            {
                Assert.DoesNotContain("POS-", note, StringComparison.Ordinal);
                Assert.DoesNotContain("REC-", note, StringComparison.Ordinal);
                Assert.DoesNotContain("SRC-", note, StringComparison.Ordinal);
                Assert.DoesNotContain("TRM-", note, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheRecordKeptBesideTheDraftCarriesNoContractText()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context(APosition(1)));

            var audit = ContractCoverageAudit.Measure(
                ledger, Array.Empty<ContractCoverageAudit.PlannedSection>(), "");

            var json = System.Text.Json.JsonSerializer.Serialize(audit.ToRecord());

            // Ids and counts. Nothing a customer wrote, and nothing the model
            // wrote — this is stored and it is logged.
            Assert.Contains("\"gaps\"", json);
            Assert.DoesNotContain("Vertragliche Grundlage", json);
        }

        // ================================================= ids stay internal

        [Theory]
        [InlineData("Der Auftragnehmer erbringt die Leistung. POS-001-02", "Der Auftragnehmer erbringt die Leistung.")]
        [InlineData("Die Vergütung (REC-004) beträgt 100,00 EUR.", "Die Vergütung beträgt 100,00 EUR.")]
        [InlineData("Wartung [SRC-011, SRC-012] ist vereinbart.", "Wartung ist vereinbart.")]
        public void CoverageIdsNeverReachTheCustomer(string written, string expected)
        {
            // The prompt says not to write them, which is worth saying and not
            // worth relying on: a model asked to declare what a paragraph covers
            // will sometimes declare it in the paragraph.
            var content = new GeneratedContractContent
            {
                Sections = [new ContractSectionContent { Heading = "Test", Paragraphs = [written] }]
            };

            Assert.Contains(expected, content.ToClauseMarkdown());
            Assert.DoesNotContain("POS-0", content.ToClauseMarkdown());
            Assert.DoesNotContain("SRC-0", content.ToClauseMarkdown());
        }

        [Fact]
        public void ACustomersOwnReferenceIsNotMistakenForOneOfOurs()
        {
            var content = new GeneratedContractContent
            {
                Sections =
                [
                    new ContractSectionContent
                    {
                        Heading = "Test",
                        Paragraphs = ["Bestellnummer ABC-123 und Projektcode XY-9912 bleiben gültig."]
                    }
                ]
            };

            var markdown = content.ToClauseMarkdown();

            Assert.Contains("ABC-123", markdown);
            Assert.Contains("XY-9912", markdown);
        }

        // ---------------------------------------------------------------

        /// <summary>
        /// A position with every optional part filled, so a test can assert that
        /// none of them is quietly collapsed. Values are shaped like real ones and
        /// belong to nobody.
        /// </summary>
        private static ManualPositionDto APosition(int number) => new()
        {
            Position = number,
            Title = $"Leistungspaket {number}",
            ServiceType = "Dienstleistung",
            Description = $"Beschreibung des Leistungspakets {number}.",
            Scope = $"Umfang des Leistungspakets {number}.",
            Deliverables = [$"Ergebnis {number}A", $"Ergebnis {number}B"],
            AcceptanceCriteria = [$"Prüfpunkt {number}"],
            CustomerResponsibilities = [$"Beistellung {number}"],
            Assumptions = [$"Annahme {number}"],
            Exclusions = [$"Nicht enthalten {number}"],
            Notes = $"Hinweis {number}.",
            DeliveryMethod = "Remote",
            Quantity = 1,
            Unit = "Pauschale",
            PricingModel = PricingModel.Fixed,
            UnitPrice = 1000m + number,
            Currency = "EUR",
            VatRate = 19m,
            BillingCycle = BillingCycle.Monthly,
            DurationPeriods = 12
        };

        internal static ContractGenerationContext Context(params ManualPositionDto[] positions) =>
            new()
            {
                Provider = new ContractGenerationContext.PartyContext("Anbieter GmbH", "Musterweg 1\n10000 Musterstadt"),
                Customer = new ContractGenerationContext.PartyContext("Kunde AG", "Beispielstraße 2\n20000 Beispielstadt"),
                Project = new ContractGenerationContext.ProjectContext("Projekt Alpha", "Beschreibung des Projekts."),
                Contract = new ContractGenerationContext.ContractDetailsContext(
                    "C-0000-000001", "EUR",
                    new DateOnly(2030, 1, 1), new DateOnly(2030, 12, 31)),
                Positions = positions,
                Totals = PositionTotalsDto.From(positions)
            };

        /// <summary>Every literal the ledger asks for, so a test can isolate one gap at a time.</summary>
        private static string Everything(ContractCoverageLedger ledger) =>
            string.Join(" ", ledger.Items.SelectMany(i => i.Evidence));
    }
}

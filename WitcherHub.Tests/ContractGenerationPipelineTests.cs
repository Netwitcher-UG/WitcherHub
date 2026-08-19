using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The contract is as long as its content, and never longer or shorter.
    ///
    /// v2 asked one model call for a fixed list of seven headings, so every
    /// contract came back with at most seven short clauses — one page, whether
    /// three things had been agreed or thirty. The length was decided by the
    /// prompt, which is the one place it must never be decided.
    ///
    /// The stages here are exercised with a scripted assistant. Nothing calls
    /// OpenAI, nothing needs a key, and the assertions are about the shape of what
    /// the pipeline does with an answer rather than about any particular wording.
    /// </summary>
    public class ContractGenerationPipelineTests
    {
        // ============================================== the scripted assistant

        /// <summary>
        /// Answers by purpose rather than by call order, so a test does not break
        /// when a stage is added — and can say "truncate the section calls"
        /// without counting.
        /// </summary>
        private sealed class ScriptedAi : IAiTextGenerator
        {
            private readonly Func<string, string, AiCompletion> _answer;

            public ScriptedAi(Func<string, string, AiCompletion> answer) => _answer = answer;

            public List<string> Purposes { get; } = new();
            public List<AiRequest> Requests { get; } = new();

            public Task<string> GenerateTextAsync(string prompt) =>
                Task.FromResult(_answer("", prompt).Text);

            public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
            {
                Purposes.Add(request.Purpose ?? "");
                Requests.Add(request);
                return Task.FromResult(_answer(request.Purpose ?? "", request.Prompt));
            }
        }

        private static ContractGenerationPipeline Pipeline(IAiTextGenerator ai, int budget = 16000) =>
            new(ai, new OpenAIOptions { Model = "test-model", MaxOutputTokens = budget }, NullLogger.Instance);

        // ================================================ length follows content

        [Fact]
        public async Task AContractIsAsLongAsThePlanItsContentRequires()
        {
            // Twelve sections — nearly twice what the old prompt could ever
            // produce, and the pipeline neither caps it nor pads it.
            var headings = Enumerable.Range(1, 12).Select(n => $"Abschnitt {n}").ToList();

            var ai = new ScriptedAi((purpose, prompt) => purpose switch
            {
                "contract.outline" => Answer(Plan(headings)),
                "contract.sections" => Answer(Written(HeadingsIn(prompt, headings))),
                _ => Answer(Plan(headings))
            });

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            Assert.Equal(12, outcome.Content.Sections.Count);

            // And it took several calls to write them: one answer carrying the
            // whole document is what made every contract short.
            Assert.True(
                ai.Purposes.Count(p => p == "contract.sections") >= 3,
                $"sections were written in {ai.Purposes.Count(p => p == "contract.sections")} calls");
        }

        [Fact]
        public async Task NothingInThePipelineNamesASectionCount()
        {
            // The property the seven-heading prompt broke: a plan of two sections
            // produces two, a plan of twenty produces twenty, and no number in the
            // code decides either.
            foreach (var count in new[] { 2, 9, 20 })
            {
                var headings = Enumerable.Range(1, count).Select(n => $"Abschnitt {n}").ToList();

                var ai = new ScriptedAi((purpose, prompt) => purpose == "contract.sections"
                    ? Answer(Written(HeadingsIn(prompt, headings)))
                    : Answer(Plan(headings)));

                var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

                Assert.Equal(count, outcome.Content.Sections.Count);
            }
        }

        [Fact]
        public async Task EverythingAgreedIsPlannedEvenWhenThePlanForgetsIt()
        {
            // A plan that assigns nothing. Left alone that is a contract which
            // mentions none of the positions — which is exactly the reported bug,
            // arrived at from the other direction.
            var ai = new ScriptedAi((purpose, prompt) => purpose == "contract.sections"
                ? Answer(Written(HeadingsIn(prompt, ["Gegenstand"])))
                : Answer(Plan(["Gegenstand"], covers: [])));

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            // Put somewhere rather than dropped, and the shortfall is recorded
            // rather than papered over.
            Assert.True(outcome.Telemetry.UnplannedEntries > 0);
            Assert.Contains(outcome.Content.Sections, s => s.HasContent);
        }

        // ==================================================== truncation

        [Fact]
        public async Task ABatchThatRanOutOfRoomIsSplitRatherThanAccepted()
        {
            var headings = new List<string> { "A", "B", "C", "D" };
            var truncateWhileMoreThanOne = true;

            var ai = new ScriptedAi((purpose, prompt) =>
            {
                if (purpose != "contract.sections") return Answer(Plan(headings));

                var asked = HeadingsIn(prompt, headings);

                // Cut off while the request is large; fits once it is small — which
                // is the behaviour a real output limit has.
                return asked.Count > 1 && truncateWhileMoreThanOne
                    ? new AiCompletion(Written(asked), AiFinishReason.Length)
                    : Answer(Written(asked));
            });

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            // All four survive, and the split is visible in the telemetry rather
            // than being a silent retry.
            Assert.Equal(4, outcome.Content.Sections.Count);
            Assert.True(outcome.Telemetry.TruncatedCalls > 0);
        }

        [Fact]
        public async Task APlanThatWasCutOffIsRefusedRatherThanGuessedAt()
        {
            // Half a plan parses as nothing, and "the assistant answered with
            // something unusable" sends the owner looking in the wrong place. The
            // message names the setting.
            var ai = new ScriptedAi((_, _) => new AiCompletion("{\"sections\": [{\"head", AiFinishReason.Length));

            var failure = await Assert.ThrowsAsync<AiInvocationException>(
                () => Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1))));

            Assert.Equal(AiFailureKind.BadResponse, failure.Kind);
            Assert.Contains(OpenAIOptions.MaxOutputTokensSettingName, failure.Message);
        }

        [Fact]
        public async Task NothingUsableAtAllIsAFailureRatherThanAnEmptyContract()
        {
            var ai = new ScriptedAi((purpose, _) => purpose == "contract.sections"
                ? Answer("{\"sections\": []}")
                : Answer(Plan(["Gegenstand"])));

            await Assert.ThrowsAsync<AiInvocationException>(
                () => Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1))));
        }

        // ======================================================== the repair

        [Fact]
        public async Task GapsBringOneRepairPassAndOnlyOne()
        {
            var ai = new ScriptedAi((purpose, prompt) => purpose switch
            {
                "contract.outline" => Answer(Plan(["Gegenstand"])),

                // Says nothing about anything, so the audit finds gaps.
                "contract.sections" => Answer(Written(["Gegenstand"], text: "Allgemeines.")),

                _ => Answer(Written(["Gegenstand"], text: "Allgemeines."))
            });

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            // Exactly one. Regenerating until the audit is happy would loop on a
            // model that cannot state a figure, and cost money each time round.
            Assert.Equal(1, ai.Purposes.Count(p => p == "contract.repair"));
            Assert.True(outcome.Telemetry.Repaired);
        }

        [Fact]
        public async Task AContractThatCoversEverythingIsNotRepaired()
        {
            var ledger = ContractCoverageLedger.FromRecord(ContractCoverageTests.Context(APosition(1)));
            var everything = string.Join(" ", ledger.Items.SelectMany(i => i.Evidence));
            var allIds = ledger.Items.Select(i => i.Id).ToList();

            var ai = new ScriptedAi((purpose, _) => purpose == "contract.outline"
                ? Answer(Plan(["Gegenstand"], covers: allIds))
                : Answer(Written(["Gegenstand"], text: everything, covers: allIds)));

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            Assert.DoesNotContain("contract.repair", ai.Purposes);
            Assert.True(outcome.Audit.IsComplete, outcome.Audit.Summary);
            Assert.False(outcome.Telemetry.Repaired);
        }

        [Fact]
        public async Task WhatIsStillMissingAfterTheRepairIsSaidRatherThanHidden()
        {
            var ai = new ScriptedAi((purpose, _) => purpose == "contract.outline"
                ? Answer(Plan(["Gegenstand"]))
                : Answer(Written(["Gegenstand"], text: "Allgemeines.")));

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            Assert.False(outcome.Audit.IsComplete);
            Assert.NotEmpty(outcome.Audit.ReviewNotes());

            // The version is produced — refusing to save it would lose the work —
            // but nobody is told it covers what it does not.
            Assert.NotEmpty(outcome.Content.Sections);
        }

        // ======================================================= the request

        [Fact]
        public async Task EveryCallCarriesTheRulesAndABudget()
        {
            var ai = new ScriptedAi((purpose, prompt) => purpose == "contract.sections"
                ? Answer(Written(HeadingsIn(prompt, ["Gegenstand"])))
                : Answer(Plan(["Gegenstand"])));

            await Pipeline(ai, budget: 12345).RunAsync(ContractCoverageTests.Context(APosition(1)));

            Assert.NotEmpty(ai.Requests);

            foreach (var request in ai.Requests)
            {
                // The system instruction was written for v2 and never sent: the
                // interface took one string and had nowhere to put it.
                Assert.False(string.IsNullOrWhiteSpace(request.SystemInstruction));
                Assert.Contains("never invent", request.SystemInstruction!, StringComparison.OrdinalIgnoreCase);

                // And no call goes out without a ceiling on the answer.
                Assert.Equal(12345, request.MaxOutputTokens);
                Assert.False(string.IsNullOrWhiteSpace(request.Purpose));
            }
        }

        [Fact]
        public async Task ThePastedDocumentIsReadBeforeAnythingIsPlanned()
        {
            var context = new ContractGenerationContext
            {
                Provider = new ContractGenerationContext.PartyContext("Anbieter GmbH", null),
                Customer = new ContractGenerationContext.PartyContext("Kunde AG", null),
                Project = new ContractGenerationContext.ProjectContext("Projekt"),
                Contract = new ContractGenerationContext.ContractDetailsContext("C-1", "EUR", null, null),
                Positions = [APosition(1)],
                SourceText = "Eine alte Vereinbarung über laufende Betreuung und Schulungen."
            };

            var ai = new ScriptedAi((purpose, prompt) => purpose switch
            {
                "contract.source-analysis" => Answer(
                    """
                    {"topics":[{"topic":"Betreuung","detail":"Laufende Betreuung"},
                               {"topic":"Schulung","detail":"Schulungen vereinbart"}]}
                    """),
                "contract.sections" => Answer(Written(HeadingsIn(prompt, ["Gegenstand"]))),
                _ => Answer(Plan(["Gegenstand"]))
            });

            var outcome = await Pipeline(ai).RunAsync(context);

            // Read first, and its topics are things the contract now has to
            // account for — rather than the document being pasted into the
            // contract, which is what used to happen.
            Assert.Equal("contract.source-analysis", ai.Purposes[0]);
            Assert.Equal(2, outcome.Telemetry.SourceTopics);
            Assert.Equal(2, outcome.Ledger.Items.Count(i => i.Origin == CoverageOrigin.Source));
        }

        [Fact]
        public async Task ADocumentThatCannotBeReadDoesNotStopTheContract()
        {
            var context = new ContractGenerationContext
            {
                Provider = new ContractGenerationContext.PartyContext("Anbieter GmbH", null),
                Customer = new ContractGenerationContext.PartyContext("Kunde AG", null),
                Project = new ContractGenerationContext.ProjectContext("Projekt"),
                Contract = new ContractGenerationContext.ContractDetailsContext("C-1", "EUR", null, null),
                Positions = [APosition(1)],
                SourceText = "Etwas, das nicht gelesen werden konnte."
            };

            var ai = new ScriptedAi((purpose, prompt) => purpose switch
            {
                "contract.source-analysis" => Answer("sorry, I cannot"),
                "contract.sections" => Answer(Written(HeadingsIn(prompt, ["Gegenstand"]))),
                _ => Answer(Plan(["Gegenstand"]))
            });

            var outcome = await Pipeline(ai).RunAsync(context);

            // Everything in the record is still there; only the pasted document's
            // topics are missing, and that is recorded.
            Assert.True(outcome.Telemetry.SourceAnalysisFailed);
            Assert.NotEmpty(outcome.Content.Sections);
        }

        // ===================================================== the telemetry

        [Fact]
        public async Task WhatIsRecordedAboutARunCarriesNoTextAtAll()
        {
            var ai = new ScriptedAi((purpose, prompt) => purpose == "contract.sections"
                ? Answer(Written(HeadingsIn(prompt, ["Gegenstand"]), text: "Vertrauliche Kundenangabe."))
                : Answer(Plan(["Gegenstand"])));

            var outcome = await Pipeline(ai).RunAsync(ContractCoverageTests.Context(APosition(1)));

            var json = JsonSerializer.Serialize(outcome.Telemetry.ToRecord());

            // This is written to the platform log and stored on the draft.
            Assert.DoesNotContain("Vertrauliche", json);
            Assert.DoesNotContain("Kunde AG", json);
            Assert.Contains("modelCalls", json);
            Assert.Contains("coverageRatio", json);
        }

        // ---------------------------------------------------------------

        private static AiCompletion Answer(string text) => new(text, AiFinishReason.Stop, 100, 200);

        private static string Plan(IReadOnlyList<string> headings, IReadOnlyList<string>? covers = null)
        {
            var sections = headings.Select((h, i) => new
            {
                heading = h,
                intent = "Test",
                // All the ids on the first section when a set is given, so a test
                // can say "the plan covers everything" without knowing the ids.
                covers = i == 0 ? covers ?? Array.Empty<string>().ToList() : new List<string>()
            });

            return JsonSerializer.Serialize(new
            {
                contractType = "Dienstleistungsvertrag",
                preamble = (string?)null,
                sections
            });
        }

        private static string Written(
            IReadOnlyList<string> headings,
            string text = "Der Auftragnehmer erbringt die vereinbarten Leistungen.",
            IReadOnlyList<string>? covers = null) =>
            JsonSerializer.Serialize(new
            {
                sections = headings.Select((h, i) => new
                {
                    heading = h,
                    paragraphs = new[] { text },
                    items = Array.Empty<string>(),
                    covers = i == 0 ? covers ?? Array.Empty<string>().ToList() : new List<string>()
                })
            });

        /// <summary>
        /// Which sections this particular call was asked for, read out of the
        /// prompt — so the scripted assistant answers the question it was asked
        /// rather than always returning the whole plan.
        /// </summary>
        private static List<string> HeadingsIn(string prompt, IReadOnlyList<string> known) =>
            known.Where(h => prompt.Contains($"HEADING: {h}\n", StringComparison.Ordinal)
                          || prompt.Contains($"HEADING: {h}\r\n", StringComparison.Ordinal)).ToList();

        private static WitcherHub.Application.Models.DTO.Contracts.ManualPositionDto APosition(int n) => new()
        {
            Position = n,
            Title = $"Leistungspaket {n}",
            Scope = $"Umfang {n}",
            Deliverables = [$"Ergebnis {n}"],
            PricingModel = Infrastructure.Data.Models.Enums.PricingModel.Fixed,
            UnitPrice = 1000m + n,
            Currency = "EUR"
        };
    }
}

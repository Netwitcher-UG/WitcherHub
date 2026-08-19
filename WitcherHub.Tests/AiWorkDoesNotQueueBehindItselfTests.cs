using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests
{
    /// <summary>
    /// No assistant action waits on an unrelated one.
    ///
    /// Reported as "the contract is taking unusually long to read … I cannot make
    /// any AI actions in the software", after the actions had already been moved
    /// off the request thread. Moving them there was right and not enough: they
    /// all went onto one queue, and the worker reading that queue awaited each
    /// item in a single loop. One job at a time, for the whole application.
    ///
    /// So pressing Generate and then Analyse meant the reading did not begin
    /// until the writing had finished. What the user saw was a reading that
    /// "took unusually long" — and it had not started.
    ///
    /// Writing a contract had also become several model calls run one after
    /// another, each waited on in turn, when nothing about them requires an
    /// order.
    /// </summary>
    public class AiWorkDoesNotQueueBehindItselfTests
    {
        // ================================================== the shared queue

        [Fact]
        public void MoreThanOneQueuedJobMayRunAtATime()
        {
            // This was 1, and the worker that reads it was not the one wired up.
            Assert.True(new BackgroundTaskOptions().MaxConcurrency > 1);
        }

        [Fact]
        public void TheWorkerThatRunsJobsIsTheOneThatCanRunSeveral()
        {
            var di = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "DependencyInjection.cs"));

            // Two classes of this name existed. The registered one awaited each
            // item in a single loop with no concurrency option at all; the one
            // that reads MaxConcurrency was never registered, so the setting had
            // no effect on anything.
            Assert.Contains("Services.HostedServices.QueuedHostedService", di);

            Assert.False(
                File.Exists(Path.Combine(
                    TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "BackgroundTasks",
                    "QueuedHostedService.cs")),
                "the serial worker is still present and can be registered by accident");

            var worker = File.ReadAllText(Path.Combine(
                TestPaths.Repository, "WitcherHub.Infrastructure", "Services", "HostedServices",
                "QueuedHostedService.cs"));

            Assert.Contains("MaxConcurrency", worker);
            Assert.Contains("SemaphoreSlim", worker);
        }

        // ============================================ the sections, together

        [Fact]
        public async Task TheSectionsOfOneContractAreWrittenAtTheSameTime()
        {
            // Twelve sections is three batches. Run one after another they cost
            // three model calls end to end, and the user waited through all three.
            var headings = Enumerable.Range(1, 12).Select(n => $"Abschnitt {n}").ToList();

            var inFlight = 0;
            var mostAtOnce = 0;
            var gate = new object();

            var ai = new SlowAi(async (purpose, prompt) =>
            {
                if (purpose == "contract.sections")
                {
                    lock (gate) { inFlight++; mostAtOnce = Math.Max(mostAtOnce, inFlight); }

                    await Task.Delay(120);

                    lock (gate) { inFlight--; }

                    return Written(HeadingsIn(prompt, headings));
                }

                return purpose == "contract.outline" ? Plan(headings) : Written(headings);
            });

            await Pipeline(ai).RunAsync(Context());

            Assert.True(mostAtOnce > 1,
                $"the section calls still ran one at a time (most at once: {mostAtOnce})");
        }

        [Fact]
        public async Task TheSectionsStayInThePlansOrderHoweverTheyFinish()
        {
            // Whichever call returns first, the contract's §§ are the plan's, in
            // the plan's order. A document whose clauses arrived in completion
            // order would be a different document on every run.
            var headings = Enumerable.Range(1, 9).Select(n => $"Abschnitt {n}").ToList();

            var ai = new SlowAi(async (purpose, prompt) =>
            {
                if (purpose != "contract.sections")
                    return purpose == "contract.outline" ? Plan(headings) : Written(headings);

                var asked = HeadingsIn(prompt, headings);

                // Later batches answer sooner.
                await Task.Delay(asked.Contains("Abschnitt 1") ? 150 : 10);

                return Written(asked);
            });

            var outcome = await Pipeline(ai).RunAsync(Context());

            Assert.Equal(headings, outcome.Content.Sections.Select(s => s.Heading).ToList());
        }

        [Fact]
        public async Task EveryCallIsCountedEvenWhenTheyRunTogether()
        {
            var headings = Enumerable.Range(1, 12).Select(n => $"Abschnitt {n}").ToList();

            var ai = new SlowAi(async (purpose, prompt) =>
            {
                await Task.Delay(5);

                return purpose switch
                {
                    "contract.outline" => Plan(headings),
                    "contract.sections" => Written(HeadingsIn(prompt, headings)),
                    _ => Written(headings)
                };
            });

            var outcome = await Pipeline(ai).RunAsync(Context());

            // count++ from several threads loses some of them, which would
            // understate the very thing these numbers exist to measure.
            Assert.Equal(ai.Calls, outcome.Telemetry.ModelCalls);
        }

        // ================================================= one fewer call

        [Fact]
        public async Task AContractThatSaysNearlyEverythingIsNotSentBackForRepair()
        {
            var ledger = ContractCoverageLedger.FromRecord(Context());

            // Everything except one entry with nothing to check literally.
            var soft = ledger.Items.Last(i => i.Evidence.Count == 0 && !i.IsCommercial);
            var covered = ledger.Items.Where(i => i.Id != soft.Id).Select(i => i.Id).ToList();
            var text = string.Join(" ", ledger.Items.SelectMany(i => i.Evidence));

            var ai = new SlowAi((purpose, _) => Task.FromResult(purpose == "contract.outline"
                ? Plan(["Gegenstand"], covered)
                : Written(["Gegenstand"], text, covered)));

            var outcome = await Pipeline(ai).RunAsync(Context());

            // A repair is another whole model call on the end of a generation
            // somebody is waiting for. One uncovered note does not earn it.
            Assert.DoesNotContain("contract.repair", ai.Purposes);
            Assert.False(outcome.Audit.IsComplete);

            // And the gap still reaches the reviewer rather than being forgotten.
            Assert.NotEmpty(outcome.Audit.ReviewNotes());
        }

        [Fact]
        public async Task AMissingFigureIsStillWorthACall()
        {
            // Coverage can be high and still be wrong in the way that matters.
            var ai = new SlowAi((purpose, _) => Task.FromResult(purpose == "contract.outline"
                ? Plan(["Gegenstand"])
                : Written(["Gegenstand"], "Allgemeines.")));

            var outcome = await Pipeline(ai).RunAsync(Context());

            Assert.Contains("contract.repair", ai.Purposes);
            Assert.True(outcome.Telemetry.Repaired);
        }

        // ================================================== a smaller prompt

        [Fact]
        public void ASectionCallIsNotSentTheWholePositionList()
        {
            var context = Context(Enumerable.Range(1, 20).Select(APosition).ToArray());
            var ledger = ContractCoverageLedger.FromRecord(context);

            var planned = new ContractOutline.PlannedSection
            {
                Heading = "Gegenstand",
                Intent = "x",
                Covers = ledger.Items.Take(4).Select(i => i.Id).ToList()
            };

            var sections = ContractGeneratorPrompt.Sections(context, ledger, [planned]);
            var outline = ContractGeneratorPrompt.Outline(context, ledger);

            // The plan needs the whole record to plan against.
            Assert.Contains("billingCycle", outline);

            // A call writing four clauses does not: what those clauses must cover
            // is listed in full below it, and it came from those same positions.
            // Sending all twenty every time is a large input paid for on every
            // call, three or four times per contract.
            Assert.DoesNotContain("billingCycle", sections);
            Assert.DoesNotContain("acceptanceCriteria", sections);

            // But the frame it has to stay consistent with is still there.
            Assert.Contains("provider", sections);
            Assert.Contains("totals", sections);

            Assert.True(sections.Length < outline.Length,
                $"the section prompt ({sections.Length}) is not smaller than the plan's ({outline.Length})");
        }

        // ---------------------------------------------------------------

        private sealed class SlowAi(Func<string, string, Task<string>> answer) : IAiTextGenerator
        {
            public List<string> Purposes { get; } = new();
            public int Calls => Purposes.Count;

            public Task<string> GenerateTextAsync(string prompt) => answer("", prompt);

            public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
            {
                lock (Purposes) Purposes.Add(request.Purpose ?? "");

                var text = await answer(request.Purpose ?? "", request.Prompt);

                return new AiCompletion(text, AiFinishReason.Stop, 10, 20);
            }
        }

        private static ContractGenerationPipeline Pipeline(IAiTextGenerator ai) =>
            new(ai, new OpenAIOptions { Model = "test-model" }, NullLogger.Instance);

        private static ContractGenerationContext Context(params ManualPositionDto[] positions) =>
            ContractCoverageTests.Context(positions.Length == 0 ? [APosition(1)] : positions);

        private static ManualPositionDto APosition(int n) => new()
        {
            Position = n,
            Title = $"Leistungspaket {n}",
            Scope = $"Umfang {n}",
            Deliverables = [$"Ergebnis {n}"],
            AcceptanceCriteria = [$"Prüfpunkt {n}"],
            PricingModel = Infrastructure.Data.Models.Enums.PricingModel.Fixed,
            UnitPrice = 1000m + n,
            Currency = "EUR"
        };

        private static string Plan(IReadOnlyList<string> headings, IReadOnlyList<string>? covers = null) =>
            JsonSerializer.Serialize(new
            {
                contractType = "Dienstleistungsvertrag",
                sections = headings.Select((h, i) => new
                {
                    heading = h,
                    intent = "Test",
                    covers = i == 0 ? covers ?? [] : new List<string>()
                })
            });

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
                    covers = i == 0 ? covers ?? [] : new List<string>()
                })
            });

        private static List<string> HeadingsIn(string prompt, IReadOnlyList<string> known) =>
            known.Where(h => prompt.Contains($"HEADING: {h}\n", StringComparison.Ordinal)
                          || prompt.Contains($"HEADING: {h}\r\n", StringComparison.Ordinal)).ToList();
    }
}

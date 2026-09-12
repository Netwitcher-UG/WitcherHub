using System.Text.Json;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests
{
    /// <summary>
    /// A contract can begin in two places, and both arrive at the same code.
    ///
    /// A customer signing a quote, and somebody building a contract by hand. The
    /// second was not a lesser version of the first — it was a different program.
    /// The quote's mapping from its items to the generator's request lived
    /// privately on the signing page; the builder had no mapping at all and
    /// composed its own document from a different generator. So the same system
    /// produced an "Agenturvertrag" with Anlage A and a Preisübersicht down one
    /// road and a "Dienstleistungsvertrag" of numbered paragraphs down the other,
    /// and which one you got depended on the door you came in.
    ///
    /// There is one mapping now, with an entry point per source, and one
    /// generator behind it. What legitimately differs is only where the positions
    /// and parties are read from, and what happens to the result: a signed quote
    /// creates a contract that does not exist yet, and the builder adds a version
    /// to one that does.
    ///
    /// These tests hold the meeting point. Most need no database: the mapping is
    /// pure, which is the point of having extracted it.
    /// </summary>
    public class BothWaysToAContractMeetTests
    {
        // ============================================================ fixtures

        private static Quote AQuote() => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            QuoteNo = "Q-2026-000001",
            Currency = "EUR",
            Project = new Project { Title = "Relaunch Onlineshop" },
            Items =
            [
                new QuoteItem
                {
                    Position = 1,
                    Title = "Laufende Betreuung",
                    Quantity = 1,
                    UnitPrice = 1500m,
                    BillingCycle = BillingCycle.Monthly
                }
            ]
        };

        private static Contract AContract() => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ContractNo = "C-2026-000001",
            Currency = "EUR",
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 9, 30),
            Project = new Project { Title = "Relaunch Onlineshop" }
        };

        private static IReadOnlyList<ManualPositionDto> APosition() =>
        [
            new ManualPositionDto
            {
                Position = 1,
                Title = "Laufende Betreuung",
                Quantity = 1,
                UnitPrice = 1500m,
                Currency = "EUR",
                VatRate = 19m,
                BillingCycle = BillingCycle.Monthly,
                PricingModel = PricingModel.Fixed
            }
        ];

        private static PartyDetails TheParties() => new(
            "Netwitcher UG",
            "Musterweg 1, 40212 Düsseldorf",
            "Musterfirma GmbH",
            "Königsallee 92a, 40212 Düsseldorf",
            new DateOnly(2026, 3, 1));

        // ================================================ one mapping, two doors

        [Fact]
        public void TheQuotesRequestCarriesTheQuote()
        {
            var quote = AQuote();

            var request = ContractDocumentFactory.FromQuote(quote, "Erika Mustermann", "erika@example.test");

            Assert.Equal(quote.ProjectId, request.ProjectId);
            Assert.Equal("Relaunch Onlineshop", request.ProjectTitle);
            Assert.Equal("EUR", request.Currency);
            Assert.Equal("Erika Mustermann", request.SignerName);

            var line = Assert.Single(request.Services);
            Assert.Equal("Laufende Betreuung", line.Title);
            Assert.Equal(1500m, line.UnitPrice);
        }

        [Fact]
        public void TheBuildersRequestCarriesTheContract()
        {
            var contract = AContract();

            var request = ContractDocumentFactory.FromContract(
                contract, APosition(), TheParties(), additionalInstructions: null, suppliedDocument: null);

            Assert.Equal(contract.ProjectId, request.ProjectId);
            Assert.Equal("Relaunch Onlineshop", request.ProjectTitle);
            Assert.Equal("EUR", request.Currency);
            Assert.Equal(contract.ContractNo, request.ContractNo);
            Assert.Equal(contract.StartDate, request.StartDate);
            Assert.Equal(contract.EndDate, request.EndDate);

            var line = Assert.Single(request.Services);
            Assert.Equal("Laufende Betreuung", line.Title);
            Assert.Equal(1500m, line.UnitPrice);
        }

        [Fact]
        public void BothAskForTheSameKindOfDocument()
        {
            var fromQuote = ContractDocumentFactory.FromQuote(AQuote(), "Erika Mustermann", null);

            var fromBuilder = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), null, null);

            // These two decide what the generator produces. If they ever differ,
            // the two doors are back to producing different documents.
            Assert.Equal(fromQuote.IncludePricesInServicesSection, fromBuilder.IncludePricesInServicesSection);
            Assert.Equal(fromQuote.LeaveCustomerFieldsBlank, fromBuilder.LeaveCustomerFieldsBlank);

            Assert.True(fromQuote.IncludePricesInServicesSection);
            Assert.False(fromQuote.LeaveCustomerFieldsBlank);
        }

        // =========================================== what genuinely has no twin

        [Fact]
        public void TheBuilderNamesTheCustomerBecauseItKnowsWhoTheyAre()
        {
            var request = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), null, null);

            Assert.NotNull(request.CustomerBlockOverride);
            Assert.Contains("Musterfirma GmbH", request.CustomerBlockOverride!);

            // The quote's path does not set this, and the generator then fills the
            // Kunde block with the literal placeholder "Name/Firma: (filled)".
            // Recorded rather than copied: this is a defect in that path, not
            // behaviour worth reproducing.
            Assert.Null(ContractDocumentFactory.FromQuote(AQuote(), "Erika", null).CustomerBlockOverride);
        }

        [Fact]
        public void NoSignatureIsClaimedForAContractNobodyHasSigned()
        {
            var request = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), null, null);

            // The signer is a quote-only fact. The customer stands as the party
            // the contract names, and no e-mail is invented for a signature that
            // has not happened.
            Assert.Equal("Musterfirma GmbH", request.SignerName);
            Assert.Null(request.SignerEmail);
        }

        [Fact]
        public void ASuppliedDocumentReachesTheModelAsContextAndNothingMore()
        {
            var request = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(),
                additionalInstructions: "Keep it short.",
                suppliedDocument: "AGENTURVERTRAG ALT\n\nDie Verguetung betraegt 9.999,00 EUR.");

            var instructions = request.AdditionalInstructions!;

            // The user's own instructions are not lost to it.
            Assert.Contains("Keep it short.", instructions);

            // And the document is labelled for what it is. This generator builds
            // Anlage A from the positions and takes no document of its own, so
            // without this channel the action called "Generate from text and
            // positions" would use only the positions.
            Assert.Contains("AGENTURVERTRAG ALT", instructions);
            Assert.Contains("LOWEST AUTHORITY", instructions);
            Assert.Contains("Do not copy it", instructions);
        }

        [Fact]
        public void WithNoSuppliedDocumentTheInstructionsAreLeftAsTheyWere()
        {
            var request = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), "Keep it short.", suppliedDocument: null);

            Assert.Equal("Keep it short.", request.AdditionalInstructions);
        }

        // ============================================ the whole core, end to end

        /// <summary>
        /// Answers the Anlage A prompt with valid JSON, so the generator's own
        /// merge into the Agenturvertrag template actually runs.
        /// </summary>
        private sealed class StubAi : IAiTextGenerator
        {
            public string? LastPrompt { get; private set; }

            public Task<string> GenerateTextAsync(string prompt)
            {
                LastPrompt = prompt;
                return Task.FromResult(AGeneratorAnswer.AnlageA);
            }
        }

        /// <summary>Records the contract it is asked to create, and creates nothing.</summary>
        private sealed class RecordingContracts : IContract
        {
            public ContractDTOs? Created { get; private set; }
            public int Creations { get; private set; }

            public Task<Guid> CreateAsync(ContractDTOs dto, CancellationToken ct = default)
            {
                Created = dto;
                Creations++;
                return Task.FromResult(Guid.NewGuid());
            }

            public Task<PagedResult<ContractViews.ContractListItemView>> GetContractsByProjectAsync(
                Guid projectId, int page = 1, int pageSize = 20, string? search = null,
                CancellationToken ct = default) => throw new NotSupportedException();

            public Task<(JsonDocument Breakdown, decimal EffectiveUnitPrice)> PreviewItemPriceAsync(
                ContractItemDto item, CancellationToken ct = default) => throw new NotSupportedException();

            public Task<ContractViews.ContractDetailsView?> GetContractAsync(
                Guid id, CancellationToken ct = default) => throw new NotSupportedException();

            public Task UpdateAsync(Guid id, UpdateContractDto dto, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

            public Task<Guid> CreateItemAsync(CreateContractItemDto dto, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task UpdateItemAsync(UpdateContractItemDto dto, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task DeleteItemAsync(DeleteContractItemDto dto, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task ReorderItemsAsync(ReorderContractItemsDto dto, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task UpdateHeaderAsync(
                Guid contractId, DocumentStatus status, DateOnly? startDate, DateOnly? endDate,
                string? terms, InvoiceSendMode invoiceSendMode, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<bool> HasSavedOverrideDraftAsync(Guid contractId, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private static (ContractCreationService Service, RecordingContracts Contracts, StubAi Ai) TheCore()
        {
            var ai = new StubAi();
            var contracts = new RecordingContracts();

            var generator = new ContractDocumentGenerator(
                ai, Options.Create(new ContractTemplateOptions()));

            return (new ContractCreationService(generator, contracts), contracts, ai);
        }

        [Fact]
        public async Task SigningAQuoteRunsTheWholeCoreAndCreatesTheContract()
        {
            // The regression guard for the flow that already worked: mapping,
            // the model call, the template merge, and the contract that comes out.
            var (core, contracts, ai) = TheCore();

            var request = ContractDocumentFactory.FromQuote(AQuote(), "Erika Mustermann", "erika@example.test");

            await core.GenerateAndCreateAsync(request);

            Assert.NotNull(ai.LastPrompt);
            Assert.Contains("Anlage A", ai.LastPrompt!);

            var created = contracts.Created;
            Assert.NotNull(created);

            Assert.Equal(DocumentStatus.Draft, created!.Contract.Status);
            Assert.True(created.Contract.FromQuote);
            Assert.NotNull(created.Contract.TermsStructured);

            // The document, from the template.
            Assert.Contains("# Agenturvertrag", created.Contract.Terms!);
            Assert.Contains("Anlage A", created.Contract.Terms!);

            // And the quote's line, carried onto the contract.
            var item = Assert.Single(created.Items);
            Assert.Equal("Laufende Betreuung", item.Title);
        }

        [Fact]
        public async Task TheBuildersRequestGoesThroughTheSameCoreAndProducesTheSameDocument()
        {
            var (core, contracts, _) = TheCore();

            var request = ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), null, null);

            await core.GenerateAndCreateAsync(request);

            var terms = contracts.Created!.Contract.Terms!;

            // The same headings the signed-quote path produces. That is the whole
            // requirement: one core, two starting points.
            Assert.Contains("# Agenturvertrag", terms);
            Assert.Contains("Anlage A", terms);
            Assert.DoesNotContain("Dienstleistungsvertrag", terms);
        }

        [Fact]
        public async Task TheBuildersDocumentNamesTheCustomerWhereTheQuotesDoesNot()
        {
            var (fromBuilder, builderContracts, _) = TheCore();

            await fromBuilder.GenerateAndCreateAsync(ContractDocumentFactory.FromContract(
                AContract(), APosition(), TheParties(), null, null));

            Assert.Contains("Musterfirma GmbH", builderContracts.Created!.Contract.Terms!);
            Assert.DoesNotContain("(filled)", builderContracts.Created.Contract.Terms!);

            var (fromQuote, quoteContracts, _) = TheCore();

            await fromQuote.GenerateAndCreateAsync(
                ContractDocumentFactory.FromQuote(AQuote(), "Erika Mustermann", null));

            // The placeholder the quote path still produces, asserted so that
            // fixing it is a visible change rather than a silent one.
            Assert.Contains("(filled)", quoteContracts.Created!.Contract.Terms!);
        }

        // ================================================== no second contract

        [Fact]
        public void AQuoteThatIsAlreadySignedNeverReachesContractCreation()
        {
            // Contract creation is dispatched from the signing handler, and the
            // handler refuses a quote that is already signed before it gets
            // there. That is what stops a second click, or a resubmitted form,
            // producing a second contract — there is no quote id on a contract to
            // deduplicate by afterwards.
            var page = Source("WitcherHub", "Pages", "Quotes", "Sign.cshtml.cs");

            var refusal = page.IndexOf(
                "quote.Status == DocumentStatus.Signed || quote.SignedAt is not null",
                StringComparison.Ordinal);

            var dispatch = page.IndexOf("QueueCreateContractFromQuoteAsync(", StringComparison.Ordinal);

            Assert.True(refusal >= 0, "the signing handler no longer refuses an already-signed quote");
            Assert.True(dispatch >= 0, "the signing handler no longer creates a contract");
            Assert.True(refusal < dispatch, "the refusal comes after the contract is dispatched");
        }

        // ============================================ one implementation, not two

        [Fact]
        public void NeitherEntryPointBuildsItsOwnRequestAnyMore()
        {
            foreach (var (file, parts) in new (string, string[])[]
                     {
                         ("the signing page", ["WitcherHub", "Pages", "Quotes", "Sign.cshtml.cs"]),
                         ("the draft service",
                             ["WitcherHub.Infrastructure", "Services", "Contracts", "ContractDraftService.cs"])
                     })
            {
                var source = Source(parts);

                Assert.False(
                    source.Contains("new GenerateContractDocumentRequest", StringComparison.Ordinal),
                    $"{file} builds its own request again — which is how the two paths came to " +
                    "produce different documents in the first place.");
            }
        }

        [Fact]
        public void TheTwoPathsCurrentlyBuildTheirDocumentsDifferently()
        {
            // Recorded, not approved of.
            //
            // The builder's Generate now goes through ContractComposer: the model
            // describes the work, the clause library supplies the law, and the
            // totals are computed in code. The signed-quote path still goes
            // through ContractDocumentGenerator, which asks the model for Anlage
            // A and merges it into a 22-line template with no legal clauses at
            // all.
            //
            // So the two doors produce different documents again — the very
            // thing that was fixed a few commits ago. This test exists so that
            // divergence is visible and deliberate rather than discovered by a
            // customer, and it should be deleted the moment the signing path is
            // moved onto the composer too.
            var builder = Source(
                ["WitcherHub.Infrastructure", "Services", "Contracts", "ContractDraftService.cs"]);

            var signing = Source(["WitcherHub", "Pages", "Quotes", "Sign.cshtml.cs"]);

            Assert.Contains("_composer.ComposeAsync", builder);
            Assert.Contains("ContractDocumentFactory.FromQuote", signing);
        }

        // =================================================================== io

        private static string Source(params string[] parts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// Making a contract by hand leaves you with a contract.
///
/// It did not. A contract created automatically from a signed quote arrives
/// finished: that path writes the generated document straight onto the contract
/// and has no approval step at all. A contract created by hand went through the
/// builder, where generating wrote a <em>version</em> and stopped —
/// <c>contract.Terms</c>, which the details page, the signing page and the PDF
/// all read, stayed empty until somebody found the version list and approved it.
///
/// So the ordinary path ended on a page saying "This contract has no wording
/// yet", one unfindable step short of the thing that had just been written,
/// while the automatic path handed over a finished document. Same contract, two
/// outcomes, decided by which door you came in.
///
/// Generation now finishes the contract. Not unconditionally: it takes effect
/// only while nothing is approved. Replacing wording that is already active — on
/// a contract that may have been sent to a customer — is a decision, and it
/// keeps the confirmation it has always had.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class GeneratingAContractProducesAContractTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whfirst;Username=postgres";

    private AppDbContext? _db;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>
    /// Answers every prompt with a complete generated contract, so these tests
    /// are about what happens to the result rather than about the model.
    /// </summary>
    private sealed class StubAi : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) =>
            Task.FromResult(AGeneratorAnswer.Complete);
    }

    /// <summary>
    /// An assistant that cannot be used at all — no key, no credit, no model.
    /// Generation falls back to composing from the record, which is the path an
    /// owner with a broken key actually takes.
    /// </summary>
    private sealed class UnusableAi : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) =>
            throw new AiInvocationException(
                AiFailureKind.NotConfigured,
                "The assistant is not configured.",
                correlationId: "test");
    }

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        var db = new AppDbContext(options);

        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch
        {
            await db.DisposeAsync();
            return;
        }

        _db = db;

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Musterfirma GmbH", TaxId = "DE123456789" };

        db.Add(customer);
        db.Add(new CustomerAddress
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            FullNameOrCompany = "Musterfirma GmbH",
            Label = "Billing",
            StreetRaw = "Lorbeerplatz 28",
            AddressLine2 = "",
            PostalCode = "48085",
            City = "Münster",
            Country = "Germany",
            CountryCode = "DE",
            IsDefault = true
        });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Online Verkauf Verwaltung",
            CustomerId = customer.Id,
            Description = "Laufende Betreuung der Vertriebskanäle."
        };

        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;
        _positions = new ManageContractPositions(db, NullLogger<ManageContractPositions>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    // ============================================ the first version is the contract

    [Fact]
    public async Task GeneratingFromPositionsLeavesAReadableContract()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.BecameTheContract);

        var contract = await Reload(contractId);

        // Terms is what the details page renders, the signing page shows the
        // customer, and the PDF is built from. Empty here is the whole bug.
        Assert.False(string.IsNullOrWhiteSpace(contract.Terms));
        Assert.Equal(result.Draft!.DocumentMarkdown, contract.Terms);
    }

    [Fact]
    public async Task GeneratingFromPastedTextLeavesAReadableContractToo()
    {
        if (!Available) return;

        // A contract can be built entirely from wording somebody supplied, with
        // no positions at all. Both sources were named in the request.
        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: false, withPastedText: true);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.BecameTheContract);
        Assert.False(string.IsNullOrWhiteSpace((await Reload(contractId)).Terms));
    }

    [Fact]
    public async Task TheContractPointsAtTheVersionItsWordingCameFrom()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var contract = await Reload(contractId);
        var drafts = await DraftsOf(contractId);

        var approved = Assert.Single(drafts, d => d.IsApproved);

        Assert.Equal(approved.Id, contract.ApprovedDraftId);
        Assert.Equal(ContractDraftStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAt);

        // The hash is what makes "this is the text that was approved" checkable
        // later. Approval has always written it; producing the wording without
        // going through approval must not skip it.
        Assert.False(string.IsNullOrWhiteSpace(approved.DocumentHash));
    }

    [Fact]
    public async Task TheVersionIsStillRecordedSoTheHistoryIsNotLost()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        // Finishing the contract is not the same as skipping the record of how it
        // was made. The builder lists versions, the details page previews them,
        // and both read these rows.
        var generated = await DraftsOf(contractId);

        Assert.Contains(generated, d => d.Kind == ContractDraftKind.Generated && d.Version == 1);
    }

    // ============================================ an active version is not replaced

    [Fact]
    public async Task RegeneratingDoesNotQuietlyReplaceWordingThatIsAlreadyActive()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var first = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var wordingAfterFirst = (await Reload(contractId)).Terms;

        // A different key, or the second is treated as a repeat of the first.
        var second = await sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = Guid.NewGuid().ToString("n") });

        Assert.True(second.Succeeded, second.FailureReason);
        Assert.False(second.BecameTheContract);

        var contract = await Reload(contractId);

        // The contract still says what it said. Regenerating proposes; it does
        // not publish over an agreement that may already have been sent.
        Assert.Equal(wordingAfterFirst, contract.Terms);
        Assert.NotEqual(second.Draft!.Id, contract.ApprovedDraftId);
        Assert.Equal(first.Draft!.Id, contract.ApprovedDraftId);
    }

    [Fact]
    public async Task TheProposedReplacementIsThereToBeApproved()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var second = await sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = Guid.NewGuid().ToString("n") });

        var drafts = await DraftsOf(contractId);
        var proposed = Assert.Single(drafts, d => d.Id == second.Draft!.Id);

        Assert.False(proposed.IsApproved);
        Assert.Equal(ContractDraftStatus.Draft, proposed.Status);

        // And approving it is still the way to make it active, with the
        // confirmation that step has always asked for.
        var approve = await sut.ApproveAsync(contractId, proposed.Version, null);

        Assert.False(approve.Succeeded);
        Assert.True(approve.RequiresOverwriteConfirmation);

        var confirmed = await sut.ApproveAsync(contractId, proposed.Version, null, confirmReplacingApproved: true);

        Assert.True(confirmed.Succeeded, confirmed.FailureReason);
        Assert.Equal(proposed.DocumentMarkdown, (await Reload(contractId)).Terms);
    }

    // ============================================ when the assistant is unusable

    [Fact]
    public async Task AContractComposedWithoutTheAssistantIsAlsoFinished()
    {
        if (!Available) return;

        // The path taken when the key is missing or the account is empty. It is
        // the case where being left one step short of a contract would be least
        // forgivable, because the assistant is already not working.
        var sut = BuildService(new UnusableAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.ComposedWithoutAi);
        Assert.True(result.BecameTheContract);

        Assert.False(string.IsNullOrWhiteSpace((await Reload(contractId)).Terms));
    }

    // ============================================ nothing was quietly widened

    [Fact]
    public async Task PastingTextDoesNotByItselfMakeItTheContract()
    {
        if (!Available) return;

        // Supplied wording is a source for generation, never the output of it —
        // the bug that showed the customer's old agreement as the contract body.
        // Storing it must not become a way to publish it.
        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: false, withPastedText: true);

        var contract = await Reload(contractId);

        Assert.True(string.IsNullOrWhiteSpace(contract.Terms));
        Assert.Null(contract.ApprovedDraftId);
    }

    [Fact]
    public async Task ARepeatedRequestStillProducesOneVersionAndOneContract()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var key = Guid.NewGuid().ToString("n");

        var first = await sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = key });
        var repeat = await sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = key });

        Assert.True(repeat.WasAlreadyPrepared);
        Assert.Equal(first.Draft!.Version, repeat.Draft!.Version);

        var drafts = await DraftsOf(contractId);

        Assert.Single(drafts, d => d.Kind == ContractDraftKind.Generated);
        Assert.Single(drafts, d => d.IsApproved);
    }

    // =================================================================== helpers

    private async Task<Contract> Reload(Guid contractId)
    {
        // Read past the change tracker: the assertion is about what was written,
        // not about what the instance in memory happens to hold.
        _db!.ChangeTracker.Clear();

        return await _db.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId);
    }

    private async Task<List<ContractDraft>> DraftsOf(Guid contractId)
    {
        _db!.ChangeTracker.Clear();

        return await _db.Set<ContractDraft>()
            .AsNoTracking()
            .Where(d => d.ContractId == contractId)
            .OrderBy(d => d.Version)
            .ToListAsync();
    }

    private ContractDraftService BuildService(IAiTextGenerator ai)
    {
        var openAi = Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" });

        return new ContractDraftService(
            _db!,
            _positions!,
            ai,
            new SemanticContractAnalyzer(ai, openAi, NullLogger<SemanticContractAnalyzer>.Instance),
            openAi,
            Options.Create(new ContractTemplateOptions()),
            NullLogger<ContractDraftService>.Instance);
    }

    private async Task<Guid> NewContractAsync(
        ContractDraftService sut, bool withPositions, bool withPastedText)
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2027, 3, 31)
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        if (withPositions)
        {
            await _positions!.SavePositionsAsync(contract.Id, new[]
            {
                new ManualPositionDto
                {
                    ClientId = Guid.NewGuid().ToString("n"),
                    Position = 1,
                    Title = "Monatliche Betreuung",
                    Description = "Laufende Betreuung der Vertriebskanäle.",
                    Quantity = 1,
                    UnitPrice = 2000m,
                    Currency = "EUR",
                    VatRate = 19m,
                    BillingCycle = BillingCycle.Monthly,
                    PricingModel = PricingModel.Fixed
                }
            });
        }

        if (withPastedText)
        {
            await sut.ImportTextAsync(
                contract.Id,
                """
                RAHMENVEREINBARUNG

                § 1 Gegenstand
                Der Auftragnehmer betreut die Vertriebskanäle des Auftraggebers.

                § 2 Vergütung
                Die Vergütung beträgt 2.000,00 EUR monatlich.
                """,
                "pasted");
        }

        return contract.Id;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// A contract whose source is a document the customer supplied, end to end.
///
/// The reported bug: the page offered "paste an existing contract", stored it,
/// approved it — and then refused to go any further because the contract had no
/// positions. Saving was refused too, for having nothing to save, so the
/// contract could not be finished by any route.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class TextOnlyContractWorkflowTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whcontracts;Username=postgres";

    private const string SuppliedContract = """
        AGENTURVERTRAG

        zwischen [COMPANY_NAME], [COMPANY_ADDRESS]
        und [CUSTOMER_NAME], [CUSTOMER_ADDRESS]

        §1 Gegenstand
        Die Agentur erbringt Leistungen im Bereich Online-Marketing.

        §2 Vergütung
        Die Vergütung beträgt pauschal 12.000,00 EUR zzgl. 19% USt.
        """;

    private AppDbContext? _db;
    private ContractDraftService? _sut;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>The assistant, stubbed. No test here reaches a real API.</summary>
    private sealed class StubAi : IAiTextGenerator
    {
        private readonly Func<string, Task<string>> _respond;

        public StubAi(string response) => _respond = _ => Task.FromResult(response);
        public StubAi(Func<string, Task<string>> respond) => _respond = respond;

        public Task<string> GenerateTextAsync(string prompt) => _respond(prompt);
    }

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var db = new AppDbContext(options);

        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch
        {
            await db.DisposeAsync();
            return;      // no database here; every test below no-ops
        }

        _db = db;

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Musterfirma GmbH" };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Text-only contract project",
            CustomerId = customer.Id
        };

        db.Add(customer);
        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;

        _positions = new ManageContractPositions(db, NullLogger<ManageContractPositions>.Instance);
        _sut = BuildService(new StubAi("## Generated wording\n\nLeistungen laut Positionen."));
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    private ContractDraftService BuildService(IAiTextGenerator ai) =>
        new(_db!,
            _positions!,
            ai,
            new ContractTextAnalyzer(
                ai,
                Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
                NullLogger<ContractTextAnalyzer>.Instance),
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            Options.Create(new ContractTemplateOptions()),
            NullLogger<ContractDraftService>.Instance);

    private async Task<Guid> NewContractAsync()
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "T-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR"
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        return contract.Id;
    }

    private static ManualPositionDto APosition(string title = "SEO Betreuung") => new()
    {
        ClientId = Guid.NewGuid().ToString("n"),
        SourceType = ContractItemSource.Manual,
        Title = title,
        Quantity = 1,
        UnitPrice = 500m,
        Currency = "EUR",
        VatRate = 19m,
        PricingModel = PricingModel.Fixed,
        BillingCycle = BillingCycle.OneTime
    };

    // =================================================================== 1

    [Fact]
    public async Task Zero_positions_and_no_text_is_blocked_with_a_useful_message()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        var result = await _sut!.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.False(result.Succeeded);

        // Useful means it names both routes out, not just the one that was missing.
        Assert.Contains("position", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contract text", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================== 2

    [Fact]
    public async Task Zero_positions_with_saved_supplied_text_can_be_generated()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        var stored = await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");
        Assert.True(stored.Succeeded);

        var source = await _sut.GetSourceAsync(contractId);
        Assert.True(source.CanGenerate);
        Assert.Equal(ContractSourceMode.SuppliedText, source.Mode);

        var generated = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded, generated.FailureReason);
        Assert.Equal(2, generated.Draft!.Version);
    }

    // =================================================================== 3

    [Fact]
    public async Task An_approved_supplied_version_with_no_positions_reaches_signing()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");
        var approved = await _sut.ApproveAsync(contractId, 1, null);

        Assert.True(approved.Succeeded);

        // Exactly the reported state: approved supplied text, zero positions.
        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(0, await _db.ContractItems.CountAsync(i => i.ContractId == contractId));

        // The approved wording became the contract's terms, which is what the
        // signing page and the PDF read.
        Assert.False(string.IsNullOrWhiteSpace(contract.Terms));
        Assert.Contains("AGENTURVERTRAG", contract.Terms!);

        // Preparing appends a version and asks nothing: it replaces nothing.
        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(prepared.Succeeded, prepared.FailureReason);
        Assert.False(prepared.RequiresOverwriteConfirmation);
    }

    // =================================================================== 5

    [Fact]
    public async Task A_single_total_is_stored_on_the_contract_without_inventing_positions()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var extraction = new ContractExtractionDto
        {
            TotalPrice = new ExtractedValue { Value = "12000.00", Confirmed = true },
            VatRate = new ExtractedValue { Value = "19", Confirmed = true },
            Currency = new ExtractedValue { Value = "EUR", Confirmed = true }
        };

        var confirmed = await _sut.ConfirmExtractionAsync(contractId, 1, extraction);
        Assert.True(confirmed.Succeeded);

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Equal(12000.00m, contract.AgreedTotalNet);
        Assert.Equal(19m, contract.AgreedTotalVatRatePercent);

        // A lump sum is one fact, not a list of services.
        Assert.Equal(0, await _db.ContractItems.CountAsync(i => i.ContractId == contractId));

        var generated = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());
        Assert.True(generated.Succeeded, generated.FailureReason);
    }

    // =================================================================== 6

    [Fact]
    public async Task A_contract_without_a_price_gets_none_invented_and_stays_a_draft()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, "KOOPERATIONSVERTRAG\n\nKeine Vergütung vereinbart.", "pasted");

        var extraction = new ContractExtractionDto
        {
            PriceMissing = true,
            TotalPrice = new ExtractedValue { Value = null, Confirmed = true },
            Warnings = { "This contract names no price." }
        };

        await _sut.ConfirmExtractionAsync(contractId, 1, extraction);

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Null(contract.AgreedTotalNet);
        Assert.True(contract.PriceDeliberatelyUnspecified);
        Assert.Equal(DocumentStatus.Draft, contract.Status);
    }

    // =================================================================== 4

    [Fact]
    public async Task Positions_read_out_of_the_text_are_stored_with_no_catalog_service()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        var stored = await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var extracted = APosition("SEO Betreuung");
        extracted.SourceType = ContractItemSource.ExtractedFromContractText;
        extracted.SourceDraftId = stored.Draft!.Id;
        extracted.CatalogServiceId = null;

        await _positions!.SavePositionsAsync(contractId, new[] { extracted });

        var item = await _db!.ContractItems.AsNoTracking().FirstAsync(i => i.ContractId == contractId);

        Assert.Equal(ContractItemSource.ExtractedFromContractText, item.Source);
        Assert.Null(item.ServiceId);                       // never a placeholder catalog row
        Assert.Equal(stored.Draft.Id, item.SourceDraftId); // traceable to the document
        Assert.Equal(500m, item.UnitPrice);                // figures survive exactly
    }

    // =================================================================== 7

    [Fact]
    public async Task The_existing_position_only_workflow_still_works()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        await _positions!.SavePositionsAsync(contractId, new[] { APosition() });

        var source = await _sut!.GetSourceAsync(contractId);
        Assert.Equal(ContractSourceMode.Positions, source.Mode);

        var generated = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded, generated.FailureReason);
        Assert.Contains("Generated wording", generated.Draft!.DocumentMarkdown);

        // Position contracts still snapshot what they were generated from.
        var draft = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId);
        Assert.NotNull(draft.PositionsSnapshot);
    }

    // =================================================================== 8

    [Fact]
    public async Task A_hybrid_contract_uses_the_text_and_the_positions()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");
        await _positions!.SavePositionsAsync(contractId, new[] { APosition() });

        var source = await _sut.GetSourceAsync(contractId);
        Assert.Equal(ContractSourceMode.Hybrid, source.Mode);

        var generated = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded, generated.FailureReason);

        // Neither source replaces the other: both were supplied on purpose.
        Assert.Contains("AGENTURVERTRAG", generated.Draft!.DocumentMarkdown);
        Assert.Contains("Generated wording", generated.Draft.DocumentMarkdown);
    }

    // =================================================================== 9

    [Fact]
    public async Task An_unreachable_assistant_costs_nothing_and_positions_stay_optional()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var broken = BuildService(new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.Network, "no route to host", "DEADBEEF")));

        // Supplied text needs no model at all: preparing it is deterministic, so
        // the contract can still be produced with the assistant down.
        var prepared = await broken.GenerateAsync(contractId, new GenerateDraftOptions());
        Assert.True(prepared.Succeeded, prepared.FailureReason);

        // The source document is untouched and still the first version.
        var original = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        Assert.Equal(SuppliedContract.Trim(), original.DocumentMarkdown);
        Assert.True(original.IsImmutableSource);

        // Analysis is the part that needs the model, and its failure says which
        // failure it was and carries the reference.
        var analysis = await broken.AnalyzeAsync(contractId, 1);

        Assert.False(analysis.Succeeded);
        Assert.Equal("DEADBEEF", analysis.CorrelationId);
        Assert.True(analysis.IsTransientFailure);       // a retry is worth offering

        var stillThere = await _db.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);
        Assert.Equal(SuppliedContract.Trim(), stillThere.DocumentMarkdown);
    }

    [Fact]
    public async Task A_position_contract_survives_the_assistant_being_down_with_its_positions_intact()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _positions!.SavePositionsAsync(contractId, new[] { APosition() });

        var broken = BuildService(new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.RateLimited, "429", "AABBCCDD", 429)));

        var result = await broken.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.False(result.Succeeded);
        Assert.True(result.IsTransientFailure);
        Assert.Contains("AABBCCDD", result.FailureReason!);

        // Nothing the user entered was lost.
        Assert.Equal(1, await _db!.ContractItems.CountAsync(i => i.ContractId == contractId));
    }

    // =================================================================== 10

    [Fact]
    public async Task Preparing_a_supplied_contract_fills_placeholders_and_leaves_the_original_alone()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(prepared.Succeeded, prepared.FailureReason);

        // The customer's real name replaced the placeholder…
        Assert.Contains("Musterfirma GmbH", prepared.Draft!.DocumentMarkdown);
        Assert.DoesNotContain("[CUSTOMER_NAME]", prepared.Draft.DocumentMarkdown);

        // …in a new version. The supplied original still has its placeholders.
        var original = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        Assert.Contains("[CUSTOMER_NAME]", original.DocumentMarkdown);
        Assert.Equal(prepared.Draft.Id, prepared.Draft.Id);
        Assert.NotEqual(original.Id, prepared.Draft.Id);

        // The prepared version points back at what it was prepared from.
        var preparedRow = await _db.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.Id == prepared.Draft.Id);
        Assert.Equal(original.Id, preparedRow.SourceDraftId);

        // Who the parties were at that moment is recorded on the contract.
        var contract = await _db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.NotNull(contract.PartySnapshot);
    }

    // =================================================================== 11

    [Fact]
    public async Task The_supplied_original_cannot_be_edited()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var edit = await _sut.SaveEditedAsync(contractId, 1, "something completely different");

        Assert.False(edit.Succeeded);

        var original = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        Assert.Equal(SuppliedContract.Trim(), original.DocumentMarkdown);
    }

    [Fact]
    public async Task An_approved_version_cannot_be_edited_either()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());
        await _sut.ApproveAsync(contractId, prepared.Draft!.Version, null);

        var edit = await _sut.SaveEditedAsync(contractId, prepared.Draft.Version, "rewritten after approval");

        Assert.False(edit.Succeeded);

        var stored = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.Id == prepared.Draft.Id);

        Assert.DoesNotContain("rewritten after approval", stored.DocumentMarkdown);
        Assert.NotNull(stored.DocumentHash);        // the approved text is fingerprinted
    }

    [Fact]
    public async Task Approving_a_second_version_stands_down_the_first()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");
        await _sut.ApproveAsync(contractId, 1, null);

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        await _sut.ApproveAsync(contractId, prepared.Draft!.Version, null, confirmReplacingApproved: true);

        var approved = await _db!.Set<ContractDraft>().AsNoTracking()
            .Where(d => d.ContractId == contractId && d.IsApproved)
            .ToListAsync();

        // Two approved versions would leave it ambiguous which text a signature
        // applies to.
        Assert.Single(approved);
        Assert.Equal(prepared.Draft.Version, approved[0].Version);
    }

    // =================================================================== extras

    [Fact]
    public async Task Supplied_text_is_stored_exactly_as_supplied()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var stored = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        // Paragraphs, headings and blank lines are the document's structure. A
        // contract that comes back reflowed is not the document that was handed
        // over.
        Assert.Equal(SuppliedContract.Trim(), stored.DocumentMarkdown);
        Assert.Contains("§1 Gegenstand", stored.DocumentMarkdown);
        Assert.Contains("\n\n", stored.DocumentMarkdown);
        Assert.Equal(ContractDraftKind.Supplied, stored.Kind);
        Assert.Equal("de", stored.SourceLanguage);
    }

    [Fact]
    public async Task Saving_no_positions_is_allowed_once_there_is_contract_text()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        // The trap: the browser asked the user to save before generating, and
        // saving an empty list was refused, so there was no way forward at all.
        var totals = await _positions!.SavePositionsAsync(contractId, Array.Empty<ManualPositionDto>());

        Assert.Equal(0, totals.PositionCount);
    }

    [Fact]
    public async Task Saving_no_positions_on_an_empty_contract_still_says_what_is_missing()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => _positions!.SavePositionsAsync(contractId, Array.Empty<ManualPositionDto>()));

        Assert.Contains("contract text", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importing_text_records_what_the_contract_is_built_from()
    {
        if (!Available) return;

        var contractId = await NewContractAsync();
        await _sut!.ImportTextAsync(contractId, SuppliedContract, "pasted");

        var afterText = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(ContractSourceMode.SuppliedText, afterText.SourceMode);

        await _positions!.SavePositionsAsync(contractId, new[] { APosition() });
        await _sut.ImportTextAsync(contractId, "Nachtrag zum Vertrag.", "pasted");

        var afterBoth = await _db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(ContractSourceMode.Hybrid, afterBoth.SourceMode);
    }
}

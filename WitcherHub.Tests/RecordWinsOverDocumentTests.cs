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
/// Which source wins when the supplied document and the record disagree.
///
/// Stated by the owner: "the details in the text is not the priority. The
/// details in the project is the priority. when the details of the project
/// empty, we can take the details from the pasted text."
///
/// Confirming a reading used to write it straight over the contract, so a start
/// date somebody had entered was silently replaced by whatever the PDF said,
/// with no way back and nothing on screen saying it had happened. The document
/// is a source for the gaps, not an authority over the decisions.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class RecordWinsOverDocumentTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whprecedence;Username=postgres";

    private const string SuppliedContract = """
        AGENTURVERTRAG

        § 2 Vergütung
        Die Vergütung beträgt 2.500,00 EUR monatlich zzgl. gesetzlicher Umsatzsteuer.
        Laufzeitbeginn: 01.08.2026. Laufzeitende: 31.03.2027.
        """;

    private AppDbContext? _db;
    private ContractDraftService? _sut;
    private Guid _projectId;

    private bool Available => _db is not null;

    private sealed class StubAi : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) => Task.FromResult("## Wording");
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

        var customer = new Customer { Id = Guid.NewGuid(), Name = "LS harbring" };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Online Verkauf Verwaltung",
            CustomerId = customer.Id
        };

        db.Add(customer);
        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;

        var ai = new StubAi();

        _sut = new ContractDraftService(
            db,
            new ManageContractPositions(db, NullLogger<ManageContractPositions>.Instance),
            ai,
            new SemanticContractAnalyzer(
                ai,
                Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
                NullLogger<SemanticContractAnalyzer>.Instance),
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            Options.Create(new ContractTemplateOptions()),
            NullLogger<ContractDraftService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    // =================================================================== 1

    [Fact]
    public async Task A_date_already_on_the_contract_survives_a_document_that_says_otherwise()
    {
        if (!Available) return;

        // The contract already says what was agreed.
        var contractId = await NewContractAsync(
            start: new DateOnly(2026, 1, 1),
            end: new DateOnly(2026, 12, 31));

        await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },
            EndDate = new ExtractedValue { Value = "31.03.2027", Confirmed = true }
        });

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Equal(new DateOnly(2026, 1, 1), contract.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), contract.EndDate);
    }

    [Fact]
    public async Task An_empty_date_is_filled_from_the_document()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(start: null, end: null);

        await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },
            EndDate = new ExtractedValue { Value = "31.03.2027", Confirmed = true }
        });

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Equal(new DateOnly(2026, 8, 1), contract.StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), contract.EndDate);
    }

    [Fact]
    public async Task Keeping_the_record_is_reported_rather_than_done_silently()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(
            start: new DateOnly(2026, 1, 1),
            end: null,
            total: 9_000m);

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },
            TotalPrice = new ExtractedValue { Value = "2500,00", Confirmed = true }
        });

        // The user ticked both. Being told "confirmed" while neither moved would
        // be a lie, so both are named.
        Assert.Contains("start date", result.KeptFromRecord);
        Assert.Contains("total price", result.KeptFromRecord);
    }

    [Fact]
    public async Task Agreeing_with_the_record_is_not_reported_as_a_conflict()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(start: new DateOnly(2026, 8, 1), end: null);

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            // The same date the contract already carries.
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true }
        });

        Assert.DoesNotContain("start date", result.KeptFromRecord);
    }

    [Fact]
    public async Task An_unconfirmed_reading_never_fills_anything()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(start: null, end: null);

        await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            // Read from the document, but not ticked by a person.
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = false }
        });

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Null(contract.StartDate);
    }

    // =================================================================== 2
    // The project gets what it does not have

    [Fact]
    public async Task A_project_with_no_dates_takes_them_from_the_contract()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(start: null, end: null);

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },
            EndDate = new ExtractedValue { Value = "31.03.2027", Confirmed = true }
        });

        var project = await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == _projectId);

        Assert.Equal(new DateOnly(2026, 8, 1), project.StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), project.EndDate);

        Assert.Contains("start date", result.FilledOnProject);
        Assert.Contains("end date", result.FilledOnProject);
    }

    [Fact]
    public async Task A_project_that_already_has_dates_keeps_them()
    {
        if (!Available) return;

        var project = await _db!.Set<Project>().FirstAsync(p => p.Id == _projectId);
        project.StartDate = new DateOnly(2025, 5, 5);
        project.EndDate = new DateOnly(2025, 6, 6);
        await _db.SaveChangesAsync();

        var contractId = await NewContractAsync(start: null, end: null);

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },
            EndDate = new ExtractedValue { Value = "31.03.2027", Confirmed = true }
        });

        var after = await _db.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == _projectId);

        // The project is the priority. Nothing read out of a document may move it.
        Assert.Equal(new DateOnly(2025, 5, 5), after.StartDate);
        Assert.Equal(new DateOnly(2025, 6, 6), after.EndDate);

        Assert.Empty(result.FilledOnProject);
    }

    // =================================================================== 3
    // The reading itself is never discarded

    [Fact]
    public async Task What_the_document_said_is_still_stored_even_when_it_did_not_win()
    {
        if (!Available) return;

        var contractId = await NewContractAsync(start: new DateOnly(2026, 1, 1), end: null);

        await _sut!.ConfirmExtractionAsync(contractId, 1, new ContractExtractionDto
        {
            StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true }
        });

        var stored = await _sut.GetExtractionAsync(contractId, 1);

        // Losing to the record is not the same as being wrong, and a later
        // reader has to be able to see what the document actually said.
        Assert.Equal("01.08.2026", stored!.StartDate.Value);
    }

    // ---------------------------------------------------------------

    private async Task<Guid> NewContractAsync(
        DateOnly? start, DateOnly? end, decimal? total = null)
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR",
            StartDate = start,
            EndDate = end,
            AgreedTotalNet = total
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        await _sut!.ImportTextAsync(contract.Id, SuppliedContract, "pasted");

        return contract.Id;
    }
}

using System.Text.Json;
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
/// The generic pipeline, as the application actually calls it.
///
/// The semantic analyser, the domain terms, the validator and the financial engine
/// were all built and tested, and none of them ran: the draft service still used
/// the older fixed-field analyser, so nothing the generic work produced ever
/// reached a contract. These tests drive the real service, against a real
/// database, with the assistant stubbed — the wiring is the thing under test.
///
/// Skips silently when no database is reachable, like the other DB-backed suites.
/// </summary>
public class SemanticAnalysisWiringTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whcontracts;Username=postgres";

    private const string SuppliedContract = """
        WARTUNGSVERTRAG

        §1 Leistungen
        Laufende Betreuung der Systeme.

        §2 Vergütung
        Die monatliche Pauschale beträgt 500,00 EUR.
        Zusätzliche Arbeiten werden mit 95,00 EUR je Stunde abgerechnet.
        """;

    private AppDbContext? _db;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>The assistant, stubbed. No test here reaches a real API.</summary>
    private sealed class StubAi(Func<string, Task<string>> respond) : IAiTextGenerator
    {
        public StubAi(string response) : this(_ => Task.FromResult(response)) { }

        public Task<string> GenerateTextAsync(string prompt) => respond(prompt);
    }

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options);

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

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Musterfirma GmbH" };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Semantic analysis project",
            CustomerId = customer.Id
        };

        db.Add(customer);
        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private ContractDraftService Service(IAiTextGenerator ai) =>
        new(_db!,
            new ManageContractPositions(_db!, NullLogger<ManageContractPositions>.Instance),
            ai,
            new SemanticContractAnalyzer(
                ai,
                Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
                NullLogger<SemanticContractAnalyzer>.Instance),
            Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
            Options.Create(new ContractTemplateOptions()),
            NullLogger<ContractDraftService>.Instance);

    /// <param name="months">
    /// The contract's own term. A monthly charge with no end date cannot be
    /// totalled against nothing, and the engine refuses to invent a length — so a
    /// contract with no term is a genuinely different case, and both are tested.
    /// </param>
    private async Task<Guid> NewContractWithSuppliedTextAsync(int? months = null)
    {
        var start = new DateOnly(2026, 1, 1);

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "S-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR",
            StartDate = months is null ? null : start,
            EndDate = months is null ? null : start.AddMonths(months.Value)
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        await Service(new StubAi("unused")).ImportTextAsync(contract.Id, SuppliedContract, "pasted");

        return contract.Id;
    }

    /// <summary>
    /// What the analyser is asked to return: two charges, one committed monthly
    /// and one an hourly rate with no agreed hours. That pair is the whole point of
    /// the generic model — the first is contract value and the second is not.
    /// </summary>
    private const string TwoTermsResponse = """
        {
          "detectedLanguage": "de",
          "documentType": "Wartungsvertrag",
          "documentTitle": "WARTUNGSVERTRAG",
          "purpose": "Laufende Betreuung",
          "concepts": [],
          "terms": [
            {
              "key": "t1",
              "name": "Monatliche Pauschale",
              "pricingModel": "FixedAmount",
              "fixedAmount": 500.00,
              "currency": "EUR",
              "billingRecurrence": "monatlich",
              "commitment": "Committed",
              "confidence": 0.9,
              "openQuestions": []
            },
            {
              "key": "t2",
              "name": "Zusätzliche Arbeiten",
              "pricingModel": "TimeAndMaterials",
              "unitRate": 95.00,
              "quantityUnit": "Stunde",
              "currency": "EUR",
              "commitment": "Variable",
              "confidence": 0.8,
              "openQuestions": []
            }
          ],
          "detectedParties": { "customerName": "Musterfirma GmbH" },
          "detectedContractTerms": { "billingCycle": "monatlich" },
          "openQuestions": [],
          "warnings": []
        }
        """;

    [Fact]
    public async Task Analysis_runs_through_the_semantic_pipeline_and_is_stored_whole()
    {
        if (!Available) return;

        var contractId = await NewContractWithSuppliedTextAsync(months: 12);
        var service = Service(new StubAi(TwoTermsResponse));

        var result = await service.AnalyzeAsync(contractId, version: 1);

        Assert.True(result.Succeeded);

        var draft = await _db!.Set<ContractDraft>()
            .AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        // The full reading is kept, not just the part the old screen can show.
        Assert.NotNull(draft.SemanticAnalysis);

        var stored = draft.SemanticAnalysis!.RootElement;

        Assert.Equal(
            ContractDraftService.SemanticAnalysisSchema,
            stored.GetProperty("schema").GetString());

        Assert.Equal(2, stored.GetProperty("terms").GetArrayLength());

        // And the figures came from the engine, which is the application's
        // arithmetic rather than the model's: 500 a month across the contract's
        // own twelve-month term. The hourly rate contributes nothing, because no
        // hours were agreed.
        var financials = stored.GetProperty("financials");
        Assert.Equal(6000m, financials.GetProperty("committedRecurringTotal").GetDecimal());
        Assert.Equal(500m, financials.GetProperty("committedMonthlyEquivalent").GetDecimal());
    }

    [Fact]
    public async Task The_review_screen_still_gets_a_reading_it_can_show()
    {
        if (!Available) return;

        var contractId = await NewContractWithSuppliedTextAsync(months: 12);
        var service = Service(new StubAi(TwoTermsResponse));

        await service.AnalyzeAsync(contractId, version: 1);

        var extraction = await service.GetExtractionAsync(contractId, version: 1);

        Assert.NotNull(extraction);
        Assert.Equal("WARTUNGSVERTRAG", extraction!.Title.Value);
        Assert.Equal("Musterfirma GmbH", extraction.CustomerName.Value);
        Assert.Equal(2, extraction.Positions.Count);

        // The committed monthly charge is contract value across the agreed term;
        // the hourly rate is not, and the total does not pretend otherwise.
        Assert.Equal("6000.00", extraction.TotalPrice.Value);

        // Nothing is pre-agreed.
        Assert.False(extraction.TotalPrice.Confirmed);
        Assert.All(extraction.Positions, p => Assert.False(p.Accepted));
    }

    [Fact]
    public async Task A_failed_reading_leaves_the_document_alone_and_says_so_safely()
    {
        if (!Available) return;

        // This is the path every analysis takes while the API key is missing or
        // wrong, so it matters more than the happy one right now.
        var contractId = await NewContractWithSuppliedTextAsync();

        var service = Service(new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.Authentication,
            "The assistant is not configured correctly.",
            correlationId: "TEST-1234")));

        var result = await service.AnalyzeAsync(contractId, version: 1);

        Assert.False(result.Succeeded);

        // No key, no provider payload, no stack trace — just what went wrong and a
        // reference that ties it to the log entry holding the detail.
        Assert.DoesNotContain("sk-", result.FailureReason ?? "");
        Assert.DoesNotContain("Bearer", result.FailureReason ?? "");
        Assert.Equal("TEST-1234", result.CorrelationId);

        var contract = await _db!.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId);
        var draft = await _db.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        Assert.Equal(ContractSourceState.AnalysisFailed, contract.SourceState);
        Assert.Equal(ContractExtractionStatus.Failed, draft.ExtractionStatus);

        // The document the user supplied is untouched, so they can carry on.
        Assert.Contains("WARTUNGSVERTRAG", draft.DocumentMarkdown);
        Assert.Null(draft.SemanticAnalysis);
    }

    [Fact]
    public async Task A_reading_that_finds_no_committed_price_does_not_invent_one()
    {
        if (!Available) return;

        var onlyHourly = """
            {
              "detectedLanguage": "de",
              "concepts": [],
              "terms": [
                {
                  "key": "t1",
                  "name": "Beratung",
                  "pricingModel": "TimeAndMaterials",
                  "unitRate": 120.00,
                  "quantityUnit": "Stunde",
                  "currency": "EUR",
                  "commitment": "Variable",
                  "confidence": 0.8,
                  "openQuestions": []
                }
              ],
              "detectedParties": {},
              "detectedContractTerms": {},
              "openQuestions": [],
              "warnings": []
            }
            """;

        var contractId = await NewContractWithSuppliedTextAsync();

        await Service(new StubAi(onlyHourly)).AnalyzeAsync(contractId, version: 1);

        var extraction = await Service(new StubAi("unused"))
            .GetExtractionAsync(contractId, version: 1);

        // A rate is not a total. The contract has no committed value and says so.
        Assert.False(extraction!.TotalPrice.HasValue);
        Assert.True(extraction.PriceMissing);
        Assert.Contains(extraction.Warnings, w => w.Contains("names no committed price"));

        var contract = await _db!.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Null(contract.AgreedTotalNet);
    }
}

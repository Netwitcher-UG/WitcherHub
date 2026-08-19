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
/// Pasted text is an input to generation and never the output of it.
///
/// Reported as the preview showing two documents: a wall of the customer's old
/// agreement, a horizontal rule, and then the actual contract underneath. It was
/// not a rendering problem. Two of the three generation paths put the pasted
/// document into the contract body — Hybrid did `source + "---" + generated`,
/// and SuppliedText copied the merged source and appended a terms block — so the
/// contract genuinely contained both.
///
/// There is one path now: every source is assembled into a context, the model
/// writes clauses from it, and the document is composed around those clauses.
/// The pasted text informs the clauses and is referenced by id, and that is all.
///
/// These tests exist so the concatenation cannot come back quietly. Several of
/// them assert the absence of something, which is unusual and deliberate: the
/// bug was an addition, so the guard has to be against addition.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class SourceIsNotTheContractTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whsource;Username=postgres";

    /// <summary>
    /// A pasted document with several distinctive strings, so its presence in a
    /// contract can be detected however it got there.
    /// </summary>
    private const string PastedAgreement = """
        AGENTURVERTRAG ALT

        zwischen Fremdagentur GmbH, Irgendwo 1, 99999 Nirgendwo
        und Musterfirma GmbH

        § 1 Vertragsgegenstand
        Die Fremdagentur betreut die Kanaele des Auftraggebers.

        § 2 Verguetung
        Die Verguetung betraegt 9.999,00 EUR monatlich.

        § 7 Gerichtsstand
        Es gilt das Recht von Nirgendwo.
        """;

    private AppDbContext? _db;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    private sealed class StubAi : IAiTextGenerator
    {
        private readonly string _answer;

        public StubAi(string answer) => _answer = answer;

        public int Calls { get; private set; }
        public string? LastPrompt { get; private set; }

        /// <summary>
        /// Every prompt of the run, joined.
        ///
        /// Generation is several calls with different jobs: one plans the
        /// contract and is given the whole structured record, the rest write a
        /// few sections each and are given only what those sections must cover.
        /// Asserting against whichever happened to be last was a test of call
        /// order, and the sections now run together so there is no reliable last
        /// one at all.
        /// </summary>
        public string Everything => string.Join("\n\n", _prompts);

        private readonly List<string> _prompts = new();

        public Task<string> GenerateTextAsync(string prompt)
        {
            lock (_prompts)
            {
                Calls++;
                LastPrompt = prompt;
                _prompts.Add(prompt);
            }

            return Task.FromResult(_answer);
        }
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
            // Every column the schema requires; the address table has several
            // that are non-nullable without a default.
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

    // =================================================================== 1
    // The pasted document does not reach the contract

    [Fact]
    public async Task ThePastedDocumentIsNotInTheGeneratedContract()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded, generated.FailureReason);

        var document = generated.Draft!.DocumentMarkdown;

        // Every distinctive string from the pasted agreement.
        foreach (var fromSource in new[]
                 {
                     "AGENTURVERTRAG ALT",
                     "Fremdagentur",
                     "Irgendwo 1",
                     "9.999,00",
                     "Gerichtsstand",
                     "Recht von Nirgendwo"
                 })
        {
            Assert.DoesNotContain(fromSource, document);
        }
    }

    [Fact]
    public async Task ThereIsExactlyOneDocumentInTheResult()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi(AGeneratorAnswer.Complete));
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var document = generated.Draft!.DocumentMarkdown;

        // One title. Two would mean two documents, which is what the owner saw.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            document, @"^# ", System.Text.RegularExpressions.RegexOptions.Multiline));

        // And no separator rule, which is how the two were joined.
        Assert.DoesNotContain("\n---\n", document);

        // § numbering starts at 1 and appears once.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(document, @"## § 1 "));
    }

    [Fact]
    public async Task TheSourceDocumentSurvivesUntouchedAsItsOwnVersion()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi(AGeneratorAnswer.Complete));
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var source = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Kind == ContractDraftKind.Supplied);

        // Separate storage, unchanged, and still available to read and edit.
        Assert.Equal(PastedAgreement.Trim(), source.DocumentMarkdown);
        Assert.True(source.IsImmutableSource);
    }

    [Fact]
    public async Task TheContractRecordsWhichSourceInformedItWithoutContainingIt()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi(AGeneratorAnswer.Complete));
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var row = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.Id == generated.Draft!.Id);

        var source = await _db.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Kind == ContractDraftKind.Supplied);

        // A reference, not content: "generated with that document in mind" stays
        // answerable without the document being in the contract.
        Assert.Equal(source.Id, row.SourceDraftId);
    }

    // =================================================================== 2
    // …but it does reach the model

    [Fact]
    public async Task ThePastedDocumentIsGivenToTheModelAsContext()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);
        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.NotNull(ai.LastPrompt);

        // It is in the request — that is the point of keeping it — and it is
        // labelled as the least authoritative thing there.
        Assert.Contains("AGENTURVERTRAG ALT", ai.LastPrompt!);
        Assert.Contains("LOWEST AUTHORITY", ai.LastPrompt!);
        Assert.Contains("Do not copy it", ai.LastPrompt!);
    }

    [Fact]
    public async Task EverySourceReachesTheModel()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);
        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        // Across the whole run, not whichever call happened to be last: the
        // plan gets the structured record, the section calls get the entries
        // they must cover, and they no longer run in a fixed order.
        var prompt = ai.Everything;

        // Company master data, from settings.
        Assert.Contains("Netwitcher", prompt);

        // Customer master data, from the customer record.
        Assert.Contains("Musterfirma GmbH", prompt);
        Assert.Contains("Lorbeerplatz 28", prompt);

        // Project data.
        Assert.Contains("Online Verkauf Verwaltung", prompt);

        // Contract details.
        Assert.Contains("EUR", prompt);

        // The positions, structured rather than flattened.
        Assert.Contains("Monatliche Betreuung", prompt);
        Assert.Contains("billingCycle", prompt);
        Assert.Contains("pricingModel", prompt);

        // And the precedence order the generator must respect.
        Assert.Contains("Where two sources disagree", prompt);
    }

    [Fact]
    public async Task OneGenerationIsOneModelCall()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);
        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task OneClickIsOneVersionEvenOnTheGeneratedPath()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var first = await sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "one" });
        var second = await sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "one" });

        // The idempotency guard used to sit inside the supplied-text path, so once
        // everything went through the model a double click meant two model calls
        // and two versions.
        Assert.Equal(first.Draft!.Version, second.Draft!.Version);
        Assert.True(second.WasAlreadyPrepared);
        Assert.Equal(1, ai.Calls);
    }

    // =================================================================== 3
    // Generation without a pasted document, and without a model

    [Fact]
    public async Task AContractCanBeGeneratedWithNoPastedTextAtAll()
    {
        if (!Available) return;

        var ai = new StubAi(AGeneratorAnswer.Complete);
        var sut = BuildService(ai);

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded, generated.FailureReason);
        Assert.StartsWith("# Dienstleistungsvertrag", generated.Draft!.DocumentMarkdown);

        // Nothing in the request pretends there was source material.
        Assert.DoesNotContain("SOURCE MATERIAL", ai.LastPrompt!);
    }

    [Fact]
    public async Task AnUnusableApiKeyStillProducesAContract()
    {
        if (!Available) return;

        var sut = BuildServiceThatThrows(new AiInvocationException(
            AiFailureKind.NotConfigured, "InvalidOperationException", "NOKEY001"));

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        // The work must not stop because OpenAI is unusable.
        Assert.True(generated.Succeeded, generated.FailureReason);
        Assert.True(generated.ComposedWithoutAi);

        var document = generated.Draft!.DocumentMarkdown;

        // Composed from the record — and still not a copy of the pasted document.
        Assert.StartsWith("# Dienstleistungsvertrag", document);
        Assert.Contains("Musterfirma GmbH", document);
        Assert.Contains("§ 1 Gegenstand des Vertrags", document);
        Assert.DoesNotContain("Fremdagentur", document);
        Assert.DoesNotContain("9.999,00", document);

        // And the figures are the ones from the positions.
        Assert.Contains("2.000,00", document);
    }

    [Fact]
    public async Task TheComposedFallbackSaysItIsPlainerAndOffersToRegenerate()
    {
        if (!Available) return;

        var sut = BuildServiceThatThrows(new AiInvocationException(
            AiFailureKind.QuotaExhausted, "ClientResultException", "NOCREDIT", 429));

        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(generated.Succeeded);

        // Nobody should mistake the plain version for the best the system can do.
        Assert.Contains("composed from your positions", generated.FailureReason!);
        Assert.Contains("regenerate", generated.FailureReason!, StringComparison.OrdinalIgnoreCase);

        // And the underlying cause is still named.
        Assert.Contains("Billing", generated.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================== 4
    // Regeneration

    [Fact]
    public async Task RegeneratingAddsAVersionAndKeepsTheOldOne()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi(AGeneratorAnswer.Complete));
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: true);

        var first = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var second = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.NotEqual(first.Draft!.Version, second.Draft!.Version);

        // The earlier version is still there — nothing is silently overwritten.
        var stillThere = await _db!.Set<ContractDraft>().AsNoTracking()
            .AnyAsync(d => d.Id == first.Draft.Id);

        Assert.True(stillThere);

        // And the new version is not the old one with anything appended.
        Assert.DoesNotContain("\n---\n", second.Draft.DocumentMarkdown);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            second.Draft.DocumentMarkdown, @"^# ",
            System.Text.RegularExpressions.RegexOptions.Multiline));
    }

    // ---------------------------------------------------------------

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

    private ContractDraftService BuildServiceThatThrows(Exception failure) =>
        BuildService(new ThrowingAi(failure));

    private sealed class ThrowingAi(Exception failure) : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) => throw failure;
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
            await sut.ImportTextAsync(contract.Id, PastedAgreement, "pasted");

        return contract.Id;
    }
}

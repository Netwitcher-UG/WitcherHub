using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Application.Services.Contracts.Clauses;
using WitcherHub.Application.Services.Contracts.Language;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// A service typed in Arabic becomes a contract in German.
///
/// This is the whole feature, end to end. Before it, a scope entered in Arabic,
/// English, French, Turkish or Kurdish was detected, reported and stopped — the
/// contract could not be approved, and the owner's remedy was to retype the
/// description by hand into the builder. Detection without translation is a
/// smoke alarm with no fire brigade.
///
/// Translation now happens inside generation. What these prove is that it
/// happened, that the figures and names came through it unchanged, and that a
/// failure still stops the contract rather than letting the original language
/// out to a customer.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class AForeignLanguageServiceBecomesAGermanContractTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whfirst;Username=postgres";

    private AppDbContext? _db;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>
    /// A planner that leaves the scope in the language it was given — which is
    /// what a real one does often enough to be the reason this feature exists.
    /// </summary>
    private sealed class PlansInSourceLanguage(string scope) : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt)
        {
            if (!prompt.Contains("ERLAUBTE KLAUSELMODULE", StringComparison.Ordinal))
                return Task.FromResult(AGeneratorAnswer.Complete);

            return Task.FromResult(AGeneratorAnswer.PlanFor(prompt).Replace(
                "Laufende Betreuung der Vertriebskanaele des Auftraggebers.",
                scope,
                StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A translator that answers every field with the German the test names,
    /// echoing the ids it was given. Stands in for the model; no key, no call.
    /// </summary>
    private sealed class TranslatesTo(IReadOnlyDictionary<string, string> german) : IContractLanguageNormalizer
    {
        public IReadOnlyList<TranslatableField>? Saw { get; private set; }
        public IReadOnlyList<ProtectedValue>? Protected { get; private set; }

        public Task<ContractNormalizationResult> NormalizeAsync(
            ContractNormalizationRequest request, CancellationToken ct = default)
        {
            Saw = request.Fields;
            Protected = request.ProtectedValues;

            var text = request.Fields.ToDictionary(
                f => f.FieldId,
                f => german.TryGetValue(f.FieldId, out var value) ? value : f.SourceText,
                StringComparer.Ordinal);

            return Task.FromResult(new ContractNormalizationResult
            {
                Succeeded = true,
                Text = text,
                DetectedLanguages = request.Fields.ToDictionary(
                    f => f.FieldId, _ => "ar", StringComparer.Ordinal),
                Provenance = Provenance()
            });
        }
    }

    /// <summary>A translator that could not finish — a timeout, a dead key, no credit.</summary>
    private sealed class CannotTranslate : IContractLanguageNormalizer
    {
        public Task<ContractNormalizationResult> NormalizeAsync(
            ContractNormalizationRequest request, CancellationToken ct = default) =>
            Task.FromResult(ContractNormalizationResult.Failed(
                "Die Übersetzung konnte nicht abgeschlossen werden. Reference TEST42.", Provenance()));
    }

    private static NormalizationProvenance Provenance() =>
        new(ContractTranslationPrompt.Version, ContractTranslationPrompt.SchemaVersion,
            "test-model", DateTimeOffset.UtcNow, 1, 1);

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

    // ================================================== the three worked examples

    [Fact]
    public async Task AnArabicScopeReachesTheContractInGerman()
    {
        if (!Available) return;

        const string arabic = "إدارة حملات الإعلانات على فيسبوك وإنستغرام مع إعداد تقرير شهري";
        const string german =
            "Betreuung der Werbekampagnen auf Facebook und Instagram einschließlich der " +
            "Erstellung eines monatlichen Berichts.";

        var result = await GenerateAsync(arabic, german);

        Assert.True(result.Succeeded, result.FailureReason);

        var document = result.Draft!.DocumentMarkdown;

        Assert.Contains(german, document);
        Assert.DoesNotContain(arabic, document);

        // And it is the contract, not a draft waiting on somebody to retype it.
        Assert.True(result.BecameTheContract, string.Join(" | ", result.ReviewNotes));
    }

    [Fact]
    public async Task AnEnglishScopeReachesTheContractInGerman()
    {
        if (!Available) return;

        const string english =
            "Monthly SEO monitoring and reporting. Implementation of technical changes is not included.";
        const string german =
            "Monatliches SEO-Monitoring einschließlich Berichterstattung. Die Umsetzung " +
            "technischer Änderungen ist nicht Bestandteil der vereinbarten Leistungen.";

        var result = await GenerateAsync(english, german);

        Assert.Contains(german, result.Draft!.DocumentMarkdown);
        Assert.DoesNotContain("Implementation of technical changes", result.Draft.DocumentMarkdown);
        Assert.True(result.BecameTheContract, string.Join(" | ", result.ReviewNotes));
    }

    [Fact]
    public async Task MixedArabicAndEnglishBecomesCoherentGerman()
    {
        if (!Available) return;

        const string mixed = "إدارة Google Ads مع monthly performance report، دون ضمان عدد محدد من Leads";
        const string german =
            "Betreuung von Google Ads einschließlich eines monatlichen Performance-Berichts. " +
            "Eine bestimmte Anzahl von Leads wird nicht geschuldet.";

        var result = await GenerateAsync(mixed, german);

        Assert.Contains(german, result.Draft!.DocumentMarkdown);
        Assert.True(result.BecameTheContract, string.Join(" | ", result.ReviewNotes));
    }

    // ============================================================ what is sent

    [Fact]
    public async Task OnlyTheDescriptivePoseIsSentAndTheNamesAreProtected()
    {
        if (!Available) return;

        var translator = new TranslatesTo(new Dictionary<string, string>
        {
            ["service.0.scope"] = "Monatliche Betreuung der Vertriebskanäle."
        });

        await GenerateAsync("Monthly management of the sales channels.", translator);

        Assert.NotNull(translator.Saw);

        // The prose, and its list items, with ids that say where each came from.
        Assert.Contains(translator.Saw!, f => f.FieldId == "service.0.scope");
        Assert.Contains(translator.Saw!, f => f.FieldId.StartsWith("service.0.deliverables", StringComparison.Ordinal));

        // And nothing that is a price, a date, a party or a clause id. Those are
        // computed in code or read from the record; a translator that never sees
        // them cannot change them.
        Assert.DoesNotContain(translator.Saw!, f => f.SourceText.Contains("2.000", StringComparison.Ordinal));
        Assert.DoesNotContain(translator.Saw!, f => f.SourceText.Contains("01.08.2026", StringComparison.Ordinal));
        Assert.DoesNotContain(translator.Saw!, f => f.FieldId.Contains("clause", StringComparison.Ordinal));

        // Both parties and the contract number go as protected values.
        Assert.Contains(translator.Protected!, p => p.Value == "Musterfirma GmbH");
        Assert.Contains(translator.Protected!, p => p.Kind == ProtectedValueKind.Identifier);
    }

    [Fact]
    public async Task ThePricesAndDatesAreUntouchedByTranslation()
    {
        if (!Available) return;

        var result = await GenerateAsync(
            "Monthly management.", "Monatliche Betreuung der Vertriebskanäle.");

        var document = result.Draft!.DocumentMarkdown;

        // 2.000,00 net, 19 % of it, 2.380,00 gross — computed in code, never
        // asked of any model, and identical to what a German-only contract has.
        Assert.Contains("2.000,00 EUR", document);
        Assert.Contains("380,00 EUR", document);
        Assert.Contains("2.380,00 EUR", document);

        // And the customer's name is intact rather than translated or restyled.
        Assert.Contains("Musterfirma GmbH", document);
    }

    // ================================================= a failure does not leak

    [Fact]
    public async Task AFailedTranslationStopsTheContractInsteadOfShippingTheSourceLanguage()
    {
        if (!Available) return;

        const string arabic = "إدارة حملات الإعلانات على فيسبوك وإنستغرام";

        var result = await GenerateAsync(arabic, new CannotTranslate());

        // The draft exists and is readable, so the reviewer can see what needs
        // doing …
        Assert.True(result.Succeeded, result.FailureReason);

        // … and it is emphatically not the contract. Falling back to the source
        // is the outcome this whole feature exists to prevent: a customer asked
        // to sign a scope they cannot read.
        Assert.False(result.BecameTheContract);
        Assert.True(string.IsNullOrWhiteSpace((await Reload(result.Draft!.Id)).Terms));

        Assert.Contains(result.ReviewNotes, n => n.Contains("TEST42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HowTheGermanWasProducedIsRecordedWithTheVersion()
    {
        if (!Available) return;

        var result = await GenerateAsync("Monthly management.", "Monatliche Betreuung.");

        var row = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.Id == result.Draft!.Id);

        var report = row.GenerationReport!.RootElement.ToString();

        // Traceable: which instructions, which schema, which model, and what
        // each field was written in before.
        Assert.Contains(ContractTranslationPrompt.Version, report);
        Assert.Contains(ContractTranslationPrompt.SchemaVersion, report);
        Assert.Contains("detectedSourceLanguages", report);

        // And none of it is in the document a customer sees.
        Assert.DoesNotContain("contract-translator/", result.Draft!.DocumentMarkdown);
        Assert.DoesNotContain("PROTECTED_", result.Draft.DocumentMarkdown);
    }

    // =================================================================== helpers

    private async Task<Contract> Reload(Guid draftId)
    {
        _db!.ChangeTracker.Clear();

        var contractId = await _db.Set<ContractDraft>().AsNoTracking()
            .Where(d => d.Id == draftId).Select(d => d.ContractId).FirstAsync();

        return await _db.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId);
    }

    private Task<ContractDraftResult> GenerateAsync(string sourceScope, string germanScope) =>
        GenerateAsync(sourceScope, new TranslatesTo(new Dictionary<string, string>
        {
            ["service.0.scope"] = germanScope
        }));

    private async Task<ContractDraftResult> GenerateAsync(
        string sourceScope, IContractLanguageNormalizer normalizer)
    {
        var sut = BuildService(new PlansInSourceLanguage(sourceScope), normalizer);
        var contractId = await NewContractAsync();

        return await sut.GenerateAsync(contractId, new GenerateDraftOptions());
    }

    private ContractDraftService BuildService(
        IAiTextGenerator ai, IContractLanguageNormalizer normalizer)
    {
        var openAi = Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" });

        return new ContractDraftService(
            _db!,
            _positions!,
            ai,
            new SemanticContractAnalyzer(ai, openAi, NullLogger<SemanticContractAnalyzer>.Instance),
            openAi,
            Options.Create(new ContractTemplateOptions
            {
                ClauseLibraryApprovedVersion = ContractClauseLibrary.LibraryVersion,
                ProviderSeat = "Berlin",
                FormRequirement = "Textform",
                DocumentPrecedence = "Vertrag, Anlage A, Angebot",
                PaymentDueDays = 14
            }),
            NullLogger<ContractDraftService>.Instance,
            background: null,
            normalizer: normalizer);
    }

    private async Task<Guid> NewContractAsync()
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

        return contract.Id;
    }
}

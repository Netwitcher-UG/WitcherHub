using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Application.Services.Contracts.Clauses;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// What the language rules do to a whole contract, rather than to a string.
///
/// The unit tests next door prove the check can tell Arabic from German. These
/// prove the thing that matters: that a contract carrying untranslated wording
/// is written, stored and readable — and does not become the contract, and
/// cannot be signed, until somebody has looked at it.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class UntranslatedWordingDoesNotReachACustomerTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whfirst;Username=postgres";

    private AppDbContext? _db;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>
    /// A planner that answers with whatever scope it is told to, so a test can
    /// say "the model left this in Arabic" without owning a model.
    /// </summary>
    private sealed class PlansWith(string scope, string? preservedTerm = null) : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt)
        {
            if (!prompt.Contains("ERLAUBTE KLAUSELMODULE", StringComparison.Ordinal))
                return Task.FromResult(AGeneratorAnswer.Complete);

            var plan = AGeneratorAnswer.PlanFor(prompt)
                .Replace(
                    "Laufende Betreuung der Vertriebskanaele des Auftraggebers.",
                    scope,
                    StringComparison.Ordinal);

            if (preservedTerm is not null)
            {
                plan = plan.Replace(
                    "\"preservedTerms\": []",
                    $"\"preservedTerms\": [{{\"value\": \"{preservedTerm}\", \"reason\": \"company_name\"}}]",
                    StringComparison.Ordinal);
            }

            return Task.FromResult(plan);
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

    // ============================================ untranslated wording blocks

    [Fact]
    public async Task ArabicLeftInTheScopeStopsTheContractBecomingTheContract()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("خدمة مراقبة وتحسين ظهور الموقع في نتائج البحث"));
        var contractId = await NewContractAsync();

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        // Written and readable, so the reviewer can see exactly what is wrong.
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(result.Draft!.DocumentMarkdown));

        // And not the contract. Terms is what the signing page shows a customer.
        Assert.False(result.BecameTheContract);
        Assert.True(string.IsNullOrWhiteSpace((await Reload(contractId)).Terms));

        // The field and the text are named, rather than "something needs review".
        Assert.Contains(result.ReviewNotes, n =>
            n.Contains("Arabischer Text", StringComparison.Ordinal) &&
            n.Contains("Leistungsumfang", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnglishLeftInTheScopeStopsItToo()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith(
            "Monthly management of the client's channels, including the production " +
            "and scheduling of all content."));

        var contractId = await NewContractAsync();
        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.BecameTheContract);
        Assert.Contains(result.ReviewNotes, n => n.Contains("nglischer Text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GermanWordingGoesThrough()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith(
            "Überwachung und Optimierung der organischen Sichtbarkeit der Website."));

        var contractId = await NewContractAsync();
        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.BecameTheContract);

        Assert.Contains(
            "Überwachung und Optimierung der organischen Sichtbarkeit",
            (await Reload(contractId)).Terms!);
    }

    [Fact]
    public async Task AnArabicCompanyNameInGermanWordingIsNotATranslationFailure()
    {
        if (!Available) return;

        // The case that decides whether this is usable. The customer's company is
        // registered in Arabic script and the contract is German; refusing to
        // issue it would be refusing the customer.
        const string name = "شركة الأمل للتجارة";

        var sut = BuildService(new PlansWith(
            $"Laufende Betreuung der Vertriebskanäle der {name} im vereinbarten Umfang.",
            preservedTerm: name));

        var contractId = await NewContractAsync();
        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.BecameTheContract, string.Join(" | ", result.ReviewNotes));

        // And the name is in the contract, exactly as it was given.
        Assert.Contains(name, (await Reload(contractId)).Terms!);
    }

    // ================================================ the figures do not move

    [Fact]
    public async Task TranslationChangesNoPriceNoQuantityAndNoDate()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("خدمة شهرية"));
        var contractId = await NewContractAsync();

        var document = (await sut.GenerateAsync(contractId, new GenerateDraftOptions()))
            .Draft!.DocumentMarkdown;

        // The model never sees a price and never writes one: the figures are
        // computed in code and merged afterwards, so an untranslated contract has
        // exactly the same arithmetic as a translated one.
        Assert.Contains("2.000,00 EUR", document);
        Assert.Contains("380,00 EUR", document);
        Assert.Contains("2.380,00 EUR", document);
    }

    // ===================================================== nothing signed moves

    [Fact]
    public async Task ASignedContractIsNotTouchedByAnyOfThis()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("Überwachung der organischen Sichtbarkeit."));
        var contractId = await NewContractAsync();

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var contract = await Reload(contractId);
        var signedText = contract.Terms!;
        var signedHash = ContractDraftService.Sha256(signedText);

        // Sign it, the way the signing page does.
        await _db!.Contracts.Where(c => c.Id == contractId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, DocumentStatus.Signed)
                .SetProperty(c => c.SignedAt, DateTimeOffset.UtcNow)
                .SetProperty(c => c.SignedDocumentHash, signedHash));

        // Generating again proposes; it does not publish over a signed agreement.
        await sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = Guid.NewGuid().ToString("n") });

        var after = await Reload(contractId);

        Assert.Equal(signedText, after.Terms);
        Assert.Equal(signedHash, after.SignedDocumentHash);
        Assert.Equal(signedHash, ContractDraftService.Sha256(after.Terms!));
    }

    // =================================== a changed contract invalidates its link

    [Fact]
    public async Task ApprovingDifferentWordingRevokesTheLinkSentForTheOldText()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("Überwachung der organischen Sichtbarkeit."));
        var contractId = await NewContractAsync();

        var first = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(first.BecameTheContract);

        // A link sent to the customer for that wording.
        var link = new ContractAccessLink
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            TokenHash = ContractAccessLink.HashToken("a-token-" + Guid.NewGuid().ToString("n")),
            RecipientEmail = "kunde@example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            IssuedForDraftVersion = first.Draft!.Version
        };

        _db!.Add(link);
        await _db.SaveChangesAsync();

        // New wording, approved over it.
        var second = await sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = Guid.NewGuid().ToString("n") });

        await sut.ApproveAsync(contractId, second.Draft!.Version, null, confirmReplacingApproved: true);

        _db.ChangeTracker.Clear();

        var after = await _db.Set<ContractAccessLink>().AsNoTracking().FirstAsync(l => l.Id == link.Id);

        // The link led to the contract, not to a document, so leaving it live
        // would have shown the customer wording nobody sent them.
        Assert.NotNull(after.RevokedAtUtc);
        Assert.True(after.RevokedBecauseWordingChanged);
    }

    [Fact]
    public async Task ALinkIssuedForTheVersionBeingApprovedIsLeftAlone()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("Überwachung der organischen Sichtbarkeit."));
        var contractId = await NewContractAsync();

        var generated = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var link = new ContractAccessLink
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            TokenHash = ContractAccessLink.HashToken("same-version-" + Guid.NewGuid().ToString("n")),
            RecipientEmail = "kunde@example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            IssuedForDraftVersion = generated.Draft!.Version
        };

        _db!.Add(link);
        await _db.SaveChangesAsync();

        // Re-approving the same version is not a change of wording.
        await sut.ApproveAsync(contractId, generated.Draft.Version, null, confirmReplacingApproved: true);

        _db.ChangeTracker.Clear();

        Assert.Null((await _db.Set<ContractAccessLink>().AsNoTracking()
            .FirstAsync(l => l.Id == link.Id)).RevokedAtUtc);
    }

    [Fact]
    public async Task ALinkFromBeforeThisWasRecordedIsNotRevokedOnAGuess()
    {
        if (!Available) return;

        var sut = BuildService(new PlansWith("Überwachung der organischen Sichtbarkeit."));
        var contractId = await NewContractAsync();

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var link = new ContractAccessLink
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            TokenHash = ContractAccessLink.HashToken("legacy-" + Guid.NewGuid().ToString("n")),
            RecipientEmail = "kunde@example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            IssuedForDraftVersion = null
        };

        _db!.Add(link);
        await _db.SaveChangesAsync();

        var second = await sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = Guid.NewGuid().ToString("n") });

        await sut.ApproveAsync(contractId, second.Draft!.Version, null, confirmReplacingApproved: true);

        _db.ChangeTracker.Clear();

        // Its version is unknown, so nothing is known about whether it is stale.
        // Killing a customer's working link on a guess is its own kind of wrong.
        Assert.Null((await _db.Set<ContractAccessLink>().AsNoTracking()
            .FirstAsync(l => l.Id == link.Id)).RevokedAtUtc);
    }

    // =================================================================== helpers

    private async Task<Contract> Reload(Guid contractId)
    {
        _db!.ChangeTracker.Clear();

        return await _db.Set<Contract>().AsNoTracking().FirstAsync(c => c.Id == contractId);
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
            Options.Create(new ContractTemplateOptions
            {
                ClauseLibraryApprovedVersion = ContractClauseLibrary.LibraryVersion,
                ProviderSeat = "Berlin",
                FormRequirement = "Textform",
                DocumentPrecedence = "Vertrag, Anlage A, Angebot",
                PaymentDueDays = 14
            }),
            NullLogger<ContractDraftService>.Instance);
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

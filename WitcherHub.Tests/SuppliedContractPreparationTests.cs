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
/// Confirming the values read out of a supplied contract, preparing a draft from
/// it, and approving that draft.
///
/// The reported behaviour: "Save the confirmed values" answered "Positions
/// saved" — the wrong operation, and no evidence anything had been written.
/// "Prepare supplied contract" then asked permission to replace the approved
/// wording, on an operation that only ever appends a version, and sat on
/// "Working…" while the dialog waited.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class SuppliedContractPreparationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whprepare;Username=postgres";

    private const string SuppliedContract = """
        AGENTURVERTRAG

        zwischen [COMPANY_NAME], [COMPANY_ADDRESS]
        und [CUSTOMER_NAME], [CUSTOMER_ADDRESS]

        § 1 Vertragsgegenstand
        Der Auftraggeber beauftragt Netwitcher UG mit der laufenden operativen
        Betreuung der E-Commerce-Vertriebskanäle.

        § 2 Vergütung
        Die Vergütung beträgt 2.500,00 EUR monatlich zzgl. gesetzlicher Umsatzsteuer.
        """;

    private AppDbContext? _db;
    private ContractDraftService? _sut;
    private ManageContractPositions? _positions;
    private Guid _projectId;

    private bool Available => _db is not null;

    private sealed class StubAi : IAiTextGenerator
    {
        private readonly Func<string, Task<string>> _respond;

        public StubAi(string response) => _respond = _ => Task.FromResult(response);
        public StubAi(Func<string, Task<string>> respond) => _respond = respond;

        public int Calls { get; private set; }

        public Task<string> GenerateTextAsync(string prompt)
        {
            Calls++;
            return _respond(prompt);
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
            return;      // no database here; every test below no-ops
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
        _positions = new ManageContractPositions(db, NullLogger<ManageContractPositions>.Instance);
        _sut = BuildService(new StubAi("## Wording"));
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
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

    private async Task<Guid> NewSuppliedContractAsync()
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR"
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        await _sut!.ImportTextAsync(contract.Id, SuppliedContract, "pasted");

        return contract.Id;
    }

    private static ContractExtractionDto AConfirmedExtraction() => new()
    {
        Title = new ExtractedValue { Value = "Agenturvertrag", Confirmed = true },
        ContractType = new ExtractedValue { Value = "Dienstleistungsvertrag", Confirmed = true },
        Language = new ExtractedValue { Value = "de", Confirmed = true },
        CustomerName = new ExtractedValue { Value = "LS harbring", Confirmed = true },
        TotalPrice = new ExtractedValue { Value = "2500,00", Confirmed = true },
        Currency = new ExtractedValue { Value = "EUR", Confirmed = true },
        VatRate = new ExtractedValue { Value = "19", Confirmed = true },
        BillingCycle = new ExtractedValue { Value = "monatlich", Confirmed = true },
        PaymentSchedule = new ExtractedValue { Value = "monatlich im Voraus", Confirmed = true },
        StartDate = new ExtractedValue { Value = "01.08.2026", Confirmed = true },

        // Read but not ticked. It must not reach the contract.
        EndDate = new ExtractedValue { Value = "31.01.2027", Confirmed = false }
    };

    // =================================================================== 1

    [Fact]
    public async Task Confirmed_values_are_written_to_the_database()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        Assert.True(result.Succeeded);

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Equal(2500.00m, contract.AgreedTotalNet);
        Assert.Equal(19m, contract.AgreedTotalVatRatePercent);
        Assert.Equal("EUR", contract.Currency);
        Assert.Equal(new DateOnly(2026, 8, 1), contract.StartDate);
        Assert.Contains("monatlich im Voraus", contract.PaymentTermsText!);

        // Unticked values stay out of the contract however plausible they look.
        Assert.Null(contract.EndDate);

        // And the parties as confirmed are recorded alongside.
        Assert.NotNull(contract.PartySnapshot);
        Assert.Contains("LS harbring", contract.PartySnapshot!.RootElement.ToString());
    }

    [Fact]
    public async Task The_confirmation_status_of_every_field_survives_a_reload()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        // Read back the way the page reads it after a refresh.
        var reloaded = await _sut.GetExtractionAsync(contractId, 1);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.TotalPrice.Confirmed);
        Assert.Equal("2500,00", reloaded.TotalPrice.Value);
        Assert.False(reloaded.EndDate.Confirmed);
        Assert.Equal("31.01.2027", reloaded.EndDate.Value);
    }

    [Fact]
    public async Task The_result_counts_what_was_actually_confirmed()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        // The message is built from these, so "Positions saved" cannot happen
        // again: the numbers describe this operation and no other.
        Assert.Equal(10, result.ConfirmedFieldCount);
        Assert.Equal(11, result.StatedFieldCount);
        Assert.NotNull(result.Money);
        Assert.Equal(2500.00m, result.Money!.AgreedTotalNet);
    }

    [Fact]
    public async Task Confirming_nothing_is_reported_as_nothing_confirmed()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var nothingTicked = new ContractExtractionDto
        {
            TotalPrice = new ExtractedValue { Value = "2500,00", Confirmed = false }
        };

        var result = await _sut!.ConfirmExtractionAsync(contractId, 1, nothingTicked);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ConfirmedFieldCount);

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Null(contract.AgreedTotalNet);
        Assert.Equal(ContractReviewState.RequiresReview, contract.ReviewState);
    }

    // =================================================================== 2

    [Fact]
    public async Task Confirming_values_creates_no_position_and_no_new_version()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        var before = await _db!.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId);

        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        // Confirming values updates structured data. It is not a wording change,
        // and labelling it as another contract version made the version list
        // impossible to read.
        Assert.Equal(before, await _db.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId));
        Assert.Equal(0, await _db.ContractItems.CountAsync(i => i.ContractId == contractId));
    }

    // =================================================================== 3

    [Fact]
    public async Task Preparing_with_zero_positions_creates_a_prepared_draft()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(prepared.Succeeded, prepared.FailureReason);
        Assert.Equal(ContractDraftKind.Prepared, prepared.Draft!.Kind);
        Assert.Equal(ContractDraftStatus.Draft, prepared.Draft.Status);
        Assert.Equal("Prepared draft", prepared.Draft.KindLabel);

        // More than a copy of the pasted text: the party placeholders are filled
        // in and the confirmed terms are carried with it.
        Assert.DoesNotContain("[CUSTOMER_NAME]", prepared.Draft.DocumentMarkdown);
        Assert.Contains("LS harbring", prepared.Draft.DocumentMarkdown);
        Assert.Contains("Bestätigte Vertragsdaten", prepared.Draft.DocumentMarkdown);
        Assert.Contains("2500,00", prepared.Draft.DocumentMarkdown);

        var contract = await _db!.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(ContractPreparationState.PreparedDraft, contract.PreparationState);
    }

    [Fact]
    public async Task Preparing_needs_no_model_at_all()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var ai = new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.Authentication, "401", "AUTHFAIL", 401));

        var result = await BuildService(ai).GenerateAsync(contractId, new GenerateDraftOptions());

        // Merging parties into a document is string replacement. Making it depend
        // on the assistant would mean an invalid API key blocked a contract that
        // needs no assistant.
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(0, ai.Calls);
    }

    // =================================================================== 4

    [Fact]
    public async Task Preparing_over_an_approved_version_leaves_it_approved_and_adds_a_draft()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        // Build up to the reported state: version 4 approved.
        await _sut!.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "a" });
        await _sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "b" });
        var fourth = await _sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "c" });

        Assert.Equal(4, fourth.Draft!.Version);
        await _sut.ApproveAsync(contractId, 4, null);

        // Preparing again asks nothing and takes nothing away.
        var fifth = await _sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "d" });

        Assert.True(fifth.Succeeded, fifth.FailureReason);
        Assert.False(fifth.RequiresOverwriteConfirmation);
        Assert.Equal(5, fifth.Draft!.Version);
        Assert.Equal(ContractDraftStatus.Draft, fifth.Draft.Status);

        var version4 = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 4);

        Assert.True(version4.IsApproved);
        Assert.Equal(ContractDraftStatus.Approved, version4.Status);
        Assert.Null(version4.SupersededAt);
    }

    // =================================================================== 5

    [Fact]
    public async Task Approving_the_new_version_supersedes_the_old_one_and_keeps_it()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var second = await _sut!.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "a" });
        await _sut.ApproveAsync(contractId, second.Draft!.Version, null);

        var third = await _sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "b" });

        // Approving over an existing approval is where the replacement is
        // decided, so this is where it is confirmed.
        var refused = await _sut.ApproveAsync(contractId, third.Draft!.Version, null);

        Assert.False(refused.Succeeded);
        Assert.True(refused.RequiresOverwriteConfirmation);
        Assert.Contains("stays in the history", refused.FailureReason!, StringComparison.OrdinalIgnoreCase);

        var approved = await _sut.ApproveAsync(
            contractId, third.Draft.Version, null, confirmReplacingApproved: true);

        Assert.True(approved.Succeeded);

        var versions = await _db!.Set<ContractDraft>().AsNoTracking()
            .Where(d => d.ContractId == contractId)
            .OrderBy(d => d.Version)
            .ToListAsync();

        var old = versions.Single(d => d.Version == second.Draft.Version);
        var now = versions.Single(d => d.Version == third.Draft.Version);

        Assert.Equal(ContractDraftStatus.Superseded, old.Status);
        Assert.NotNull(old.SupersededAt);
        Assert.NotNull(old.ApprovedAt);        // still a record of having been approved
        Assert.NotNull(old.DocumentHash);      // and of exactly what text that was

        Assert.Equal(ContractDraftStatus.Approved, now.Status);
        Assert.True(now.IsApproved);

        var contract = await _db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(now.Id, contract.ApprovedDraftId);
        Assert.Equal(now.DocumentMarkdown, contract.Terms);

        // Exactly one active version, always.
        Assert.Single(versions, d => d.IsApproved);
    }

    // =================================================================== 6

    [Fact]
    public async Task An_authentication_failure_creates_no_version_and_keeps_everything_saved()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        var before = await _db!.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId);

        var ai = new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.Authentication, "401", "AUTH0001", 401));

        // Analysis is the step that needs the model.
        var analysis = await BuildService(ai).AnalyzeAsync(contractId, 1);

        Assert.False(analysis.Succeeded);

        // A rejected key will not start working on the next attempt, so a retry
        // is not offered and nothing loops.
        Assert.False(analysis.IsTransientFailure);
        Assert.Equal(1, ai.Calls);
        Assert.Contains("AUTH0001", analysis.FailureReason!);

        // No empty version, and the confirmed values are untouched.
        Assert.Equal(before, await _db.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId));

        var contract = await _db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
        Assert.Equal(2500.00m, contract.AgreedTotalNet);

        var source = await _db.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);
        Assert.Equal(SuppliedContract.Trim(), source.DocumentMarkdown);
    }

    [Fact]
    public async Task An_authentication_failure_names_the_variable_an_administrator_must_fix()
    {
        var failure = new AiInvocationException(
            AiFailureKind.NotConfigured, "no key", "CFG00001");

        // An ordinary user cannot fix this by trying again, so the message says
        // what has to change instead of suggesting a retry.
        Assert.Contains("OpenAI__ApiKey", failure.UserMessage);
        Assert.Contains("CFG00001", failure.UserMessage);
        Assert.False(failure.IsTransient);

        var rejected = new AiInvocationException(
            AiFailureKind.Authentication, "401", "AUTH0002", 401);

        Assert.False(rejected.IsTransient);
        Assert.DoesNotContain("try again", rejected.UserMessage, StringComparison.OrdinalIgnoreCase);

        await Task.CompletedTask;
    }

    // =================================================================== 7

    [Fact]
    public async Task A_timeout_leaves_no_version_behind_and_can_be_retried()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        var before = await _db!.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId);

        var timingOut = new StubAi(_ => throw new AiInvocationException(
            AiFailureKind.Timeout, "timed out", "TIME0001"));

        var analysis = await BuildService(timingOut).AnalyzeAsync(contractId, 1);

        Assert.False(analysis.Succeeded);
        Assert.True(analysis.IsTransientFailure);       // this one is worth retrying
        Assert.Equal(before, await _db.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId));

        // And the retry works.
        var recovered = await _sut!.GenerateAsync(contractId, new GenerateDraftOptions());
        Assert.True(recovered.Succeeded, recovered.FailureReason);
    }

    // =================================================================== 8

    [Fact]
    public async Task Preparing_twice_with_the_same_key_produces_one_draft()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var first = await _sut!.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = "one-click" });

        var second = await _sut.GenerateAsync(
            contractId, new GenerateDraftOptions { IdempotencyKey = "one-click" });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

        // Two clicks are one intention.
        Assert.Equal(first.Draft!.Version, second.Draft!.Version);
        Assert.True(second.WasAlreadyPrepared);
        Assert.Equal(2, await _db!.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId));
    }

    [Fact]
    public async Task A_deliberate_second_preparation_still_produces_a_second_draft()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        await _sut!.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "first" });
        var second = await _sut.GenerateAsync(contractId, new GenerateDraftOptions { IdempotencyKey = "second" });

        Assert.False(second.WasAlreadyPrepared);
        Assert.Equal(3, await _db!.Set<ContractDraft>().CountAsync(d => d.ContractId == contractId));
    }

    // =================================================================== 9

    [Fact]
    public async Task A_contract_level_total_is_reported_without_positions()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        var state = await _sut.GetStateAsync(contractId);

        Assert.Equal(2500.00m, state.Money.AgreedTotalNet);
        Assert.Equal("EUR", state.Money.Currency);
        Assert.False(state.Money.PriceDeliberatelyUnspecified);

        // The fixture leaves the end date stated but unticked, so the review is
        // genuinely only part done — and the state says so rather than rounding
        // up to "confirmed".
        Assert.Equal(ContractReviewState.PartiallyConfirmed, state.ReviewState);
    }

    // =================================================================== 10

    [Fact]
    public async Task No_total_is_reported_as_none_rather_than_as_zero()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var state = await _sut!.GetStateAsync(contractId);

        // Null, not 0. A contract whose price the system has not been told is
        // not a contract for nothing, and the screen shows "Not specified".
        Assert.Null(state.Money.AgreedTotalNet);
        Assert.False(state.Money.PriceDeliberatelyUnspecified);
    }

    // =================================================================== 11

    [Fact]
    public async Task The_prepared_draft_carries_the_confirmed_facts_unchanged()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();
        await _sut!.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var text = prepared.Draft!.DocumentMarkdown;

        // Figures appear exactly as confirmed; the original wording is intact.
        Assert.Contains("2500,00", text);
        Assert.Contains("§ 1 Vertragsgegenstand", text);
        Assert.Contains("2.500,00 EUR monatlich", text);

        // The unconfirmed end date is nowhere in it.
        Assert.DoesNotContain("31.01.2027", text);
    }

    // =================================================================== 12

    [Fact]
    public async Task Every_state_survives_a_reload()
    {
        if (!Available) return;

        var contractId = await NewSuppliedContractAsync();

        var afterImport = await _sut!.GetStateAsync(contractId);
        Assert.Equal(ContractSourceState.SuppliedTextSaved, afterImport.SourceState);
        Assert.Equal(ContractPreparationState.NoPreparedDraft, afterImport.PreparationState);

        await _sut.ConfirmExtractionAsync(contractId, 1, AConfirmedExtraction());
        var afterConfirm = await _sut.GetStateAsync(contractId);
        Assert.Equal(ContractReviewState.PartiallyConfirmed, afterConfirm.ReviewState);

        var prepared = await _sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var afterPrepare = await _sut.GetStateAsync(contractId);
        Assert.Equal(ContractPreparationState.PreparedDraft, afterPrepare.PreparationState);

        await _sut.ApproveAsync(contractId, prepared.Draft!.Version, null);

        // Read through a context that has never seen any of this, which is what a
        // page load after a restart does.
        await using var fresh = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_db!.Database.GetConnectionString())
                .Options);

        var reloaded = await fresh.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);

        Assert.Equal(ContractSourceState.SuppliedTextSaved, reloaded.SourceState);
        Assert.Equal(ContractReviewState.PartiallyConfirmed, reloaded.ReviewState);
        Assert.Equal(ContractPreparationState.PreparedDraft, reloaded.PreparationState);
        Assert.Equal(2500.00m, reloaded.AgreedTotalNet);
        Assert.NotNull(reloaded.ApprovedDraftId);

        var versions = await fresh.Set<ContractDraft>().AsNoTracking()
            .Where(d => d.ContractId == contractId)
            .OrderBy(d => d.Version)
            .ToListAsync();

        Assert.Equal(ContractDraftKind.Supplied, versions[0].Kind);
        Assert.True(versions[0].IsImmutableSource);
        Assert.Equal(ContractDraftKind.Prepared, versions[1].Kind);
        Assert.Equal(ContractDraftStatus.Approved, versions[1].Status);
    }

    // =================================================================== extras

    [Fact]
    public void Currency_written_in_words_is_normalised_to_a_code()
    {
        // Real documents say "Euro" and "€". Storing either in a field the rest of
        // the system formats as a currency code breaks every total after it.
        Assert.Equal("EUR", ContractDraftService.NormaliseCurrency("Euro"));
        Assert.Equal("EUR", ContractDraftService.NormaliseCurrency("€"));
        Assert.Equal("EUR", ContractDraftService.NormaliseCurrency(" eur "));
        Assert.Equal("CHF", ContractDraftService.NormaliseCurrency("chf"));

        // Anything unrecognisable is refused rather than stored as junk.
        Assert.Null(ContractDraftService.NormaliseCurrency("nach Vereinbarung"));
    }

    [Fact]
    public void German_dates_are_read_correctly()
    {
        Assert.True(ContractDraftService.TryParseDate("01.08.2026", out var german));
        Assert.Equal(new DateOnly(2026, 8, 1), german);

        Assert.True(ContractDraftService.TryParseDate("2026-08-01", out var iso));
        Assert.Equal(new DateOnly(2026, 8, 1), iso);

        Assert.False(ContractDraftService.TryParseDate("nach Absprache", out _));
    }
}

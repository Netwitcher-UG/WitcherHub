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
    /// Answers in whichever shape the caller asked for, so these tests are about
    /// what happens to the result rather than about the model.
    ///
    /// Two generators are reachable and they want different answers. A contract
    /// with positions goes through the composer, which asks for a plan: how the
    /// work is described and which clause modules apply. A contract with only
    /// pasted text goes through the pipeline, which asks for clauses. The prompt
    /// says which.
    /// </summary>
    private sealed class StubAi : IAiTextGenerator
    {
        public int PlannerCalls { get; private set; }

        public Task<string> GenerateTextAsync(string prompt)
        {
            if (prompt.Contains("ERLAUBTE KLAUSELMODULE", StringComparison.Ordinal))
                PlannerCalls++;

            return Task.FromResult(AGeneratorAnswer.For(prompt, AGeneratorAnswer.Complete));
        }
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

    // ======================================= the same document a signed quote makes

    [Fact]
    public async Task AContractWithPositionsIsWrittenByTheComposer()
    {
        if (!Available) return;

        var ai = new StubAi();
        var sut = BuildService(ai);
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(1, ai.PlannerCalls);

        var document = result.Draft!.DocumentMarkdown;

        // The composed document's own headings. Until now the builder produced a
        // "Dienstleistungsvertrag" of numbered paragraphs with no service
        // description and no general terms at all.
        Assert.Contains("# Agenturvertrag", document);
        Assert.Contains("## Vertragspartner", document);
        Assert.Contains("## Anlage A", document);
        Assert.Contains("## Preisübersicht", document);
        Assert.Contains("## Unterschriften", document);
    }

    [Fact]
    public async Task WhatTheModelWroteAboutTheWorkIsInTheDocument()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var document = result.Draft!.DocumentMarkdown;

        // The plan is rendered into the services section rather than merely
        // stored, which is the whole point of the one model call.
        Assert.Contains("Laufende Betreuung der Vertriebskanaele", document);
        Assert.Contains("Monatlicher Report", document);

        // And the exclusions, which are the half of a scope that stops an
        // argument later.
        Assert.Contains("Mediabudget", document);
    }

    [Fact]
    public async Task TheGeneralTermsComeFromTheLibraryRatherThanTheModel()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var document = result.Draft!.DocumentMarkdown;

        // The contract this replaced described a service and a price and
        // stopped. Every one of these is a standing clause the model is not
        // asked to write and cannot leave out.
        foreach (var title in new[]
                 {
                     ContractClauseLibrary.Find("LIABILITY_B2B")!.Title,
                     ContractClauseLibrary.Find("CONFIDENTIALITY")!.Title,
                     ContractClauseLibrary.Find("GERMAN_LAW")!.Title
                 })
        {
            Assert.Contains(title, document);
        }

        // And the one the model did choose, because the work is ongoing
        // marketing rather than a deliverable.
        Assert.Contains(
            ContractClauseLibrary.Find("SERVICE_NO_SUCCESS_GUARANTEE")!.Title, document);
    }

    [Fact]
    public async Task TheTotalsInTheDocumentAreTheOnesComputedFromThePositions()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var document = (await sut.GenerateAsync(contractId, new GenerateDraftOptions()))
            .Draft!.DocumentMarkdown;

        // 2.000,00 net, 19 % of it in tax, 2.380,00 gross. None of these three
        // numbers is ever asked of the model — a figure it produced is a figure
        // nobody agreed to.
        Assert.Contains("2.000,00 EUR", document);
        Assert.Contains("380,00 EUR", document);
        Assert.Contains("2.380,00 EUR", document);
    }

    [Fact]
    public async Task TheContractNamesTheCustomerRatherThanAPlaceholder()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());
        var document = result.Draft!.DocumentMarkdown;

        // The quote's path leaves CustomerBlockOverride unset, and the generator
        // then fills the Kunde block with the literal string "(filled)" — a
        // contract that does not say who it is between. The parties are known
        // here, so they are passed.
        Assert.Contains("Musterfirma GmbH", document);
        Assert.DoesNotContain("(filled)", document);
    }

    [Fact]
    public async Task ThePlanIsKeptAsDataBesideTheDocument()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var contract = await Reload(contractId);
        var stored = contract.TermsStructured!.RootElement.ToString();

        Assert.NotNull(contract.TermsStructured);

        // The description as data, not only as prose in the document: what was
        // classified, what was scoped, and what was excluded.
        Assert.Contains("Laufende Betreuung der Vertriebskanaele", stored);
        Assert.Contains("Mediabudget", stored);

        // Including how it was classified, which is what decided the term and
        // termination clauses and is not otherwise recoverable from the prose.
        Assert.Contains("\"overallType\": \"service\"", stored);
        Assert.Contains("\"recurrence\": \"recurring\"", stored);
    }

    [Fact]
    public async Task HowTheVersionWasProducedIsRecordedWithIt()
    {
        if (!Available) return;

        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        var row = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.Id == result.Draft!.Id);

        Assert.Equal(ContractPlannerPrompt.Version, row.PromptVersion);

        var report = row.GenerationReport!.RootElement.ToString();

        // A year from now, which instructions and which wording produced this
        // document has to still be answerable. Recorded at the time is the only
        // moment it can be.
        Assert.Contains(ContractPlannerPrompt.Version, report);
        Assert.Contains(ContractClauseLibrary.LibraryVersion, report);
        Assert.Contains("SERVICE_NO_SUCCESS_GUARANTEE", report);
    }

    [Fact]
    public async Task AContractWithOnlyPastedTextStillHasAWayToBeWritten()
    {
        if (!Available) return;

        // The Agenturvertrag generator refuses without positions — it builds
        // Anlage A out of them. A contract whose only source is a document must
        // not become unwritable because of that.
        var sut = BuildService(new StubAi());
        var contractId = await NewContractAsync(sut, withPositions: false, withPastedText: true);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(result.Draft!.DocumentMarkdown));
    }

    // ======================================= the first version is the contract

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

    // ==================================== what stands between a draft and a contract

    [Fact]
    public async Task NothingIsApprovedUntilTheLibraryIsReleased()
    {
        if (!Available) return;

        // Every module in the library ships as "pending legal review", which is
        // the honest state for wording no lawyer has seen. On a default
        // installation that is every module, so nothing auto-approves.
        var template = ReleasedInstallation();
        template.ClauseLibraryApprovedVersion = null;

        var sut = BuildService(new StubAi(), template);
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        // Written, stored and readable — and not the contract.
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(result.Draft!.DocumentMarkdown));
        Assert.False(result.BecameTheContract);

        Assert.True(string.IsNullOrWhiteSpace((await Reload(contractId)).Terms));

        // And it says so, rather than leaving somebody to wonder why the details
        // page is still empty.
        Assert.Contains(result.ReviewNotes, n => n.Contains("freigegeben", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReleaseOfADifferentVersionIsNotARelease()
    {
        if (!Available) return;

        // Legal released 0.9. The wording has changed since. Approving on the
        // strength of a review of something else is the failure this guards.
        var template = ReleasedInstallation();
        template.ClauseLibraryApprovedVersion = "0.9.0";

        var sut = BuildService(new StubAi(), template);
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        Assert.False((await sut.GenerateAsync(contractId, new GenerateDraftOptions())).BecameTheContract);
    }

    [Fact]
    public async Task AContractMissingItsAgreedTermsIsWrittenButNotPublished()
    {
        if (!Available) return;

        // No payment term, no seat, no form requirement. The clauses that need
        // them are standing clauses — every contract gets them — so a contract
        // without those values is not a contract that is merely shorter.
        var template = ReleasedInstallation();
        template.PaymentDueDays = null;
        template.ProviderSeat = null;
        template.FormRequirement = null;

        var sut = BuildService(new StubAi(), template);
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.BecameTheContract);

        // The document states no payment term rather than a plausible one.
        Assert.DoesNotContain("14 Tagen", result.Draft!.DocumentMarkdown);

        // And each missing value is named, so "not approvable" comes with a list
        // of what to do about it.
        Assert.Contains(result.ReviewNotes, n => n.Contains("PaymentDueDays", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APositionTheModelNeverDescribedStopsTheContract()
    {
        if (!Available) return;

        // The model is required to echo back the id it was given, and the
        // descriptions are matched to positions on it. An answer that echoes
        // something else produces a priced line with no scope, no deliverables
        // and no exclusions under it — a contract that does not say what is
        // being bought, and that looks entirely normal.
        var sut = BuildService(new AnswersAboutSomeOtherPosition());
        var contractId = await NewContractAsync(sut, withPositions: true, withPastedText: false);

        var result = await sut.GenerateAsync(contractId, new GenerateDraftOptions());

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.BecameTheContract);

        Assert.Contains(
            result.ReviewNotes,
            n => n.Contains("Leistungsbeschreibung", StringComparison.Ordinal) &&
                 n.Contains("Monatliche Betreuung", StringComparison.Ordinal));
    }

    /// <summary>Answers the planner with a section for a position that is not there.</summary>
    private sealed class AnswersAboutSomeOtherPosition : IAiTextGenerator
    {
        public Task<string> GenerateTextAsync(string prompt) =>
            Task.FromResult(prompt.Contains("ERLAUBTE KLAUSELMODULE", StringComparison.Ordinal)
                ? AGeneratorAnswer.Plan.Replace(
                    "\"serviceItemId\": \"\"", "\"serviceItemId\": \"eine-andere-position\"",
                    StringComparison.Ordinal)
                : AGeneratorAnswer.Complete);
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

    /// <summary>
    /// A fully configured installation: the legal decisions made, and the clause
    /// library released.
    ///
    /// Both halves are needed before a generated contract can become the
    /// contract, and both are deliberately unset in the shipped defaults. A
    /// contract that states a payment term or a Gerichtsstand nobody chose is
    /// worse than one that states neither, so the standing clauses that need those
    /// values stay out of the document and block approval until somebody
    /// configures them — which is what <see cref="NothingIsApprovedUntilTheLibraryIsReleased"/>
    /// and <see cref="AContractMissingItsAgreedTermsIsWrittenButNotPublished"/>
    /// pin down.
    /// </summary>
    private ContractDraftService BuildService(IAiTextGenerator ai) =>
        BuildService(ai, ReleasedInstallation());

    private static ContractTemplateOptions ReleasedInstallation() => new()
    {
        ClauseLibraryApprovedVersion = ContractClauseLibrary.LibraryVersion,
        ProviderSeat = "Berlin",
        FormRequirement = "Textform",
        DocumentPrecedence = "Vertrag, Anlage A, Angebot",
        PaymentDueDays = 14
    };

    private ContractDraftService BuildService(IAiTextGenerator ai, ContractTemplateOptions template)
    {
        var openAi = Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" });

        return new ContractDraftService(
            _db!,
            _positions!,
            ai,
            new SemanticContractAnalyzer(ai, openAi, NullLogger<SemanticContractAnalyzer>.Instance),
            openAi,
            Options.Create(template),
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

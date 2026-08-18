using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// Reading a supplied contract happens off the request that asked for it.
///
/// Reported as "The server did not answer in time (HTTP 502) ... And when I
/// click on the extract position from the text, I become this error. And it's
/// take too long." Reading a real contract takes longer than the platform proxy
/// will hold a connection open, so the browser was shown a gateway error while
/// the model was still working — and the reading then completed into a request
/// nobody was listening to, its answer discarded.
///
/// The reading is unchanged. What changed is that the request returns at once
/// and the outcome is written to the draft, where the page can come back for it.
/// That makes the row the only channel to the user, so these tests are mostly
/// about the row telling the truth in every state.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class AnalysisRunsInBackgroundTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whbackground;Username=postgres";

    private const string SuppliedContract = """
        AGENTURVERTRAG
        Die Vergütung beträgt 2.500,00 EUR monatlich zzgl. gesetzlicher Umsatzsteuer.
        """;

    private AppDbContext? _db;
    private Guid _projectId;

    private bool Available => _db is not null;

    /// <summary>Answers whatever the test tells it to, whenever the test says.</summary>
    private sealed class ScriptedAi : IAiTextGenerator
    {
        private readonly Func<Task<string>> _respond;

        public ScriptedAi(Func<Task<string>> respond) => _respond = respond;

        public Task<string> GenerateTextAsync(string prompt) => _respond();
    }

    /// <summary>
    /// Holds the queued work instead of running it, so a test can look at the
    /// draft while the reading is still "in flight" — the state the page polls.
    /// </summary>
    private sealed class HeldRunner : IBackgroundAnalysisRunner
    {
        private readonly Func<Guid, int, Task> _work;

        public HeldRunner(Func<Guid, int, Task> work) => _work = work;

        public bool WasCalled { get; private set; }
        public int Calls { get; private set; }

        private Guid _contractId;
        private int _version;

        public ValueTask RunAsync(Guid contractId, int version)
        {
            WasCalled = true;
            Calls++;
            _contractId = contractId;
            _version = version;
            return ValueTask.CompletedTask;
        }

        /// <summary>Lets the held reading actually happen.</summary>
        public Task ReleaseAsync() => _work(_contractId, _version);
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
        var project = new Project { Id = Guid.NewGuid(), Title = "Online Verkauf", CustomerId = customer.Id };

        db.Add(customer);
        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    // =================================================================== 1
    // Starting returns at once

    [Fact]
    public async Task Starting_an_analysis_returns_without_waiting_for_the_model()
    {
        if (!Available) return;

        // A model that never answers. If starting waited for it, this test would
        // hang — which is exactly what the request used to do until the proxy
        // gave up on it.
        var neverAnswers = new TaskCompletionSource<string>();

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => neverAnswers.Task), runner);

        var contractId = await NewSuppliedContractAsync(sut);

        var started = await sut.StartAnalysisAsync(contractId, 1)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(started.Running);
        Assert.False(started.AlreadyRunning);
        Assert.True(runner.WasCalled);
    }

    [Fact]
    public async Task While_it_runs_the_draft_says_so()
    {
        if (!Available) return;

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => Task.FromResult("{}")), runner);

        var contractId = await NewSuppliedContractAsync(sut);
        await sut.StartAnalysisAsync(contractId, 1);

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        Assert.True(progress.Running);
        Assert.False(progress.Finished);
        Assert.False(progress.Failed);
        Assert.NotNull(progress.Elapsed);
    }

    [Fact]
    public async Task Pressing_the_button_twice_does_not_pay_for_two_readings()
    {
        if (!Available) return;

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => Task.FromResult("{}")), runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        var second = await sut.StartAnalysisAsync(contractId, 1);

        Assert.True(second.Running);
        Assert.True(second.AlreadyRunning);

        // The second press joined the first reading rather than queueing another.
        Assert.Equal(1, runner.Calls);
    }

    // =================================================================== 2
    // Finishing, and failing

    [Fact]
    public async Task A_finished_reading_is_waiting_when_the_page_asks()
    {
        if (!Available) return;

        ContractDraftService? sut = null;

        var runner = new HeldRunner((id, v) => sut!.AnalyzeAsync(id, v));
        sut = BuildService(new ScriptedAi(() => Task.FromResult(AnAnalysisResponse)), runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        await runner.ReleaseAsync();

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        Assert.True(progress.Finished);
        Assert.False(progress.Running);
        Assert.NotNull(progress.Extraction);
    }

    [Fact]
    public async Task A_failure_survives_the_request_that_started_it()
    {
        if (!Available) return;

        ContractDraftService? sut = null;

        var runner = new HeldRunner((id, v) => sut!.AnalyzeAsync(id, v));

        sut = BuildService(
            new ScriptedAi(() => throw new AiInvocationException(
                AiFailureKind.QuotaExhausted, "ClientResultException", "Q7654321", 429)),
            runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        await runner.ReleaseAsync();

        // The request that asked for this is long gone. The reason has to be in
        // the row or the page has nothing to show — and a reload used to turn a
        // nameable failure back into "never analysed".
        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        Assert.True(progress.Failed);
        Assert.False(progress.Running);
        Assert.NotNull(progress.FailureReason);
        Assert.Contains("Billing", progress.FailureReason, StringComparison.OrdinalIgnoreCase);

        // And it is still there on the next page load.
        var draft = await _db!.Set<ContractDraft>().AsNoTracking()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        Assert.Equal(ContractExtractionStatus.Failed, draft.ExtractionStatus);
        Assert.NotNull(draft.ExtractionError);
        Assert.Null(draft.ExtractionStartedAt);
    }

    [Fact]
    public async Task A_fault_only_the_owner_can_clear_is_not_offered_as_retryable()
    {
        if (!Available) return;

        ContractDraftService? sut = null;
        var runner = new HeldRunner((id, v) => sut!.AnalyzeAsync(id, v));

        sut = BuildService(
            new ScriptedAi(() => throw new AiInvocationException(
                AiFailureKind.NotConfigured, "InvalidOperationException", "C0000001")),
            runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        await runner.ReleaseAsync();

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        // Moving the reading off the request made the row the only channel to
        // the user, and the first version of that flattened every failure back
        // to "transient" — the same mistake that had an owner with no API key
        // pressing a button that could not succeed. The screen uses this to
        // decide between a toast that fades and a notice that stays.
        Assert.True(progress.Failed);
        Assert.False(progress.IsTransientFailure);
        Assert.Contains("OpenAI__ApiKey", progress.FailureReason!);
    }

    [Fact]
    public async Task A_fault_that_may_pass_stays_retryable()
    {
        if (!Available) return;

        ContractDraftService? sut = null;
        var runner = new HeldRunner((id, v) => sut!.AnalyzeAsync(id, v));

        sut = BuildService(
            new ScriptedAi(() => throw new AiInvocationException(
                AiFailureKind.Timeout, "TaskCanceledException", "C0000002")),
            runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        await runner.ReleaseAsync();

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        Assert.True(progress.Failed);
        Assert.True(progress.IsTransientFailure);
    }

    [Fact]
    public async Task A_failed_reading_can_be_started_again()
    {
        if (!Available) return;

        ContractDraftService? sut = null;
        var runner = new HeldRunner((id, v) => sut!.AnalyzeAsync(id, v));

        sut = BuildService(
            new ScriptedAi(() => throw new AiInvocationException(
                AiFailureKind.Timeout, "TaskCanceledException", "T1111111")),
            runner);

        var contractId = await NewSuppliedContractAsync(sut);

        await sut.StartAnalysisAsync(contractId, 1);
        await runner.ReleaseAsync();

        var again = await sut.StartAnalysisAsync(contractId, 1);

        Assert.True(again.Running);
        Assert.False(again.AlreadyRunning);
        Assert.Equal(2, runner.Calls);
    }

    // =================================================================== 3
    // The worker that never came back

    [Fact]
    public async Task A_reading_whose_worker_died_does_not_spin_for_ever()
    {
        if (!Available) return;

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => Task.FromResult("{}")), runner);

        var contractId = await NewSuppliedContractAsync(sut);
        await sut.StartAnalysisAsync(contractId, 1);

        // The queue is in-process, so a restart loses whatever was running and
        // leaves the row saying "analysing" with nothing left to wait for.
        var draft = await _db!.Set<ContractDraft>()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        draft.ExtractionStartedAt =
            DateTimeOffset.UtcNow - ContractDraftService.AnalysisAbandonedAfter - TimeSpan.FromMinutes(1);

        await _db.SaveChangesAsync();

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);

        Assert.False(progress.Running);
        Assert.True(progress.Failed);
        Assert.True(progress.IsTransientFailure);
        Assert.Contains("start it again", progress.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_abandoned_reading_can_be_restarted()
    {
        if (!Available) return;

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => Task.FromResult("{}")), runner);

        var contractId = await NewSuppliedContractAsync(sut);
        await sut.StartAnalysisAsync(contractId, 1);

        var draft = await _db!.Set<ContractDraft>()
            .FirstAsync(d => d.ContractId == contractId && d.Version == 1);

        draft.ExtractionStartedAt =
            DateTimeOffset.UtcNow - ContractDraftService.AnalysisAbandonedAfter - TimeSpan.FromMinutes(1);

        await _db.SaveChangesAsync();

        // Restarting is the only way out, so it must not be refused as
        // "already running".
        var again = await sut.StartAnalysisAsync(contractId, 1);

        Assert.True(again.Running);
        Assert.False(again.AlreadyRunning);
    }

    // =================================================================== 4

    [Fact]
    public async Task A_version_with_no_text_is_refused_rather_than_queued()
    {
        if (!Available) return;

        var runner = new HeldRunner((_, _) => Task.CompletedTask);
        var sut = BuildService(new ScriptedAi(() => Task.FromResult("{}")), runner);

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR"
        };

        _db!.Add(contract);
        _db.Add(new ContractDraft
        {
            Id = Guid.NewGuid(),
            ContractId = contract.Id,
            Version = 1,
            DocumentMarkdown = "",
            Kind = ContractDraftKind.Supplied
        });

        await _db.SaveChangesAsync();

        var started = await sut.StartAnalysisAsync(contract.Id, 1);

        Assert.False(started.Running);
        Assert.False(runner.WasCalled);
        Assert.Contains("no contract text", started.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_a_queue_the_reading_simply_happens_inline()
    {
        if (!Available) return;

        // How the tests and any non-web caller use it: no runner, so the work is
        // done before the call returns and the answer is already waiting.
        var sut = BuildService(new ScriptedAi(() => Task.FromResult(AnAnalysisResponse)), background: null);

        var contractId = await NewSuppliedContractAsync(sut);

        var started = await sut.StartAnalysisAsync(contractId, 1);
        Assert.True(started.Running);

        var progress = await sut.GetAnalysisProgressAsync(contractId, 1);
        Assert.True(progress.Finished);
    }

    // ---------------------------------------------------------------

    /// <summary>The smallest answer the semantic analyser accepts.</summary>
    private const string AnAnalysisResponse = """
        {
          "language": "de",
          "title": "Agenturvertrag",
          "concepts": [],
          "terms": []
        }
        """;

    private ContractDraftService BuildService(IAiTextGenerator ai, IBackgroundAnalysisRunner? background)
    {
        var openAi = Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" });

        return new ContractDraftService(
            _db!,
            new ManageContractPositions(_db!, NullLogger<ManageContractPositions>.Instance),
            ai,
            new SemanticContractAnalyzer(ai, openAi, NullLogger<SemanticContractAnalyzer>.Instance),
            openAi,
            Options.Create(new ContractTemplateOptions()),
            NullLogger<ContractDraftService>.Instance,
            background);
    }

    private async Task<Guid> NewSuppliedContractAsync(ContractDraftService sut)
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

        await sut.ImportTextAsync(contract.Id, SuppliedContract, "pasted");

        return contract.Id;
    }
}

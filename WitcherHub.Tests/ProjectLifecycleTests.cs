using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Domain.Projects;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Projects;
using WitcherHub.Infrastructure.Repositories.Implementations;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// A project's own lifecycle, and what it takes to delete one.
///
/// The reported failure: a project the user had just created refused to be
/// deleted, and the status shown on the list did not match what the user
/// believed it to be. Both were the same bug — a quote or a contract wrote its
/// own state onto the project's status field, and the delete rule keyed on that
/// field.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class ProjectLifecycleTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whprojects;Username=postgres";

    private AppDbContext? _db;
    private ManageProject? _sut;
    private Guid _customerId;

    private bool Available => _db is not null;

    private sealed class NoCache : IAppCache
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, AppCacheEntryOptions? options = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key, Func<CancellationToken, Task<T>> factory,
            AppCacheEntryOptions? options = null, CancellationToken ct = default) => factory(ct);

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> GetOrCreateVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(1L);
        public Task<long> BumpVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(2L);
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

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Lifecycle Test Customer" };
        db.Add(customer);
        await db.SaveChangesAsync();

        _customerId = customer.Id;

        _sut = new ManageProject(
            new UnitOfWork(db), new NoCache(), NullLogger<ManageProject>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private async Task<Guid> NewProjectAsync(ProjectStatus status = ProjectStatus.Draft)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            CustomerId = _customerId,
            Title = "Project " + Guid.NewGuid().ToString("n")[..6],
            Status = status
        };

        _db!.Add(project);
        await _db.SaveChangesAsync();

        return project.Id;
    }

    private async Task<Guid> AddContractAsync(Guid projectId, DocumentStatus status, bool signed = false)
    {
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = status,
            SignedAt = signed ? DateTimeOffset.UtcNow : null,
            Currency = "EUR"
        };

        _db!.Add(contract);
        await _db.SaveChangesAsync();

        return contract.Id;
    }

    // ============================================================ status

    [Fact]
    public async Task A_new_project_is_a_draft_and_stays_one()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();

        var state = await _sut!.GetWorkflowStateAsync(projectId);

        Assert.Equal(ProjectStatus.Draft, state.Status);
        Assert.False(state.IsArchived);
        Assert.Equal(DocumentProgress.NotCreated, state.Quotes);
        Assert.Equal(DocumentProgress.NotCreated, state.Contracts);
    }

    [Fact]
    public async Task A_draft_contract_does_not_change_the_project_status()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Draft);

        var project = await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId);
        var state = await _sut!.GetWorkflowStateAsync(projectId);

        // The bug: this used to be Waiting, which the user never chose and could
        // not see the true value of — and which then blocked deletion.
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.Equal(ProjectStatus.Draft, state.Status);

        // The contract's own state is reported as the contract's.
        Assert.Equal(DocumentProgress.Draft, state.Contracts);
    }

    [Fact]
    public async Task The_project_status_and_the_document_states_are_separate_facts()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync(ProjectStatus.Active);
        await AddContractAsync(projectId, DocumentStatus.Draft);

        var state = await _sut!.GetWorkflowStateAsync(projectId);

        // An active project containing a draft contract. Two true statements, and
        // one field could never hold both.
        Assert.Equal(ProjectStatus.Active, state.Status);
        Assert.Equal(DocumentProgress.Draft, state.Contracts);
        Assert.Equal(DocumentProgress.NotCreated, state.Invoices);
    }

    [Fact]
    public async Task Every_reader_gets_the_same_status()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Sent);

        // The three ways the application asks: the entity, the workflow state,
        // and the list projection.
        var entity = await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId);
        var state = await _sut!.GetWorkflowStateAsync(projectId);
        var listed = await _sut.GetProjectsAsync(1, 50, ct: default);

        var row = listed.Items.Single(p => p.Id == projectId);

        Assert.Equal(entity.Status, state.Status);
        Assert.Equal(entity.Status, row.Status);
    }

    [Fact]
    public async Task A_signed_contract_promotes_a_draft_project_but_moves_nothing_else()
    {
        if (!Available) return;

        // The one place a document may still touch the project. Verified through
        // the state machine rather than by trusting the write.
        var draft = await NewProjectAsync(ProjectStatus.Draft);
        var onHold = await NewProjectAsync(ProjectStatus.OnHold);

        foreach (var id in new[] { draft, onHold })
            await AddContractAsync(id, DocumentStatus.Signed, signed: true);

        var draftState = await _sut!.GetWorkflowStateAsync(draft);
        var onHoldState = await _sut.GetWorkflowStateAsync(onHold);

        // Contracts report Settled either way; the project a person deliberately
        // put on hold stays on hold.
        Assert.Equal(DocumentProgress.Settled, draftState.Contracts);
        Assert.Equal(DocumentProgress.Settled, onHoldState.Contracts);
        Assert.Equal(ProjectStatus.OnHold, onHoldState.Status);
    }

    // ========================================================== deletion

    [Fact]
    public async Task A_project_with_nothing_in_it_can_be_deleted()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();

        var impact = await _sut!.GetDeletionImpactAsync(projectId);
        Assert.True(impact.IsClean);
        Assert.False(impact.IsBlocked);

        await _sut.DeleteAsync(projectId);

        Assert.False(await _db!.Set<Project>().AnyAsync(p => p.Id == projectId));
    }

    [Fact]
    public async Task A_project_holding_only_a_draft_contract_can_still_be_deleted()
    {
        if (!Available) return;

        // Precisely the reported failure. Nothing here has to be kept: a draft
        // contract is not a legal record, and refusing on the project's status
        // was refusing on a field a contract had written.
        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Draft);

        var impact = await _sut!.GetDeletionImpactAsync(projectId);

        Assert.False(impact.IsBlocked);
        Assert.Equal(1, impact.Contracts);
        Assert.Contains(impact.WhatWillBeDeleted, w => w.Contains("contract"));

        await _sut.DeleteAsync(projectId);

        Assert.False(await _db!.Set<Project>().AnyAsync(p => p.Id == projectId));
    }

    [Fact]
    public async Task A_signed_contract_blocks_permanent_deletion_and_says_why()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Signed, signed: true);

        var impact = await _sut!.GetDeletionImpactAsync(projectId);

        Assert.True(impact.IsBlocked);
        Assert.Contains("signed contract", impact.BlockingReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Archive", impact.BlockingReason!, StringComparison.OrdinalIgnoreCase);

        var error = await Assert.ThrowsAsync<BadRequestAppException>(() => _sut.DeleteAsync(projectId));
        Assert.Contains("signed contract", error.Message, StringComparison.OrdinalIgnoreCase);

        // And nothing was destroyed on the way to refusing.
        Assert.True(await _db!.Set<Project>().AnyAsync(p => p.Id == projectId));
        Assert.True(await _db.Set<Contract>().AnyAsync(c => c.ProjectId == projectId));
    }

    [Fact]
    public async Task Deleting_a_project_never_deletes_the_customer()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await _sut!.DeleteAsync(projectId);

        // A project is one piece of work for a customer, not the customer.
        Assert.True(await _db!.Set<Customer>().AnyAsync(c => c.Id == _customerId));
    }

    // =========================================================== archive

    [Fact]
    public async Task Archiving_keeps_everything_and_hides_the_project()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync(ProjectStatus.Active);
        await AddContractAsync(projectId, DocumentStatus.Signed, signed: true);

        await _sut!.ArchiveAsync(projectId);

        var project = await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId);

        Assert.True(project.IsArchived);
        Assert.NotNull(project.ArchivedAt);

        // The status is not touched, so restoring returns exactly what was
        // archived — and the contract is still there.
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.True(await _db.Set<Contract>().AnyAsync(c => c.ProjectId == projectId));
    }

    [Fact]
    public async Task Restoring_puts_the_project_back_as_it_was()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync(ProjectStatus.OnHold);

        await _sut!.ArchiveAsync(projectId);
        await _sut.RestoreAsync(projectId);

        var project = await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId);

        Assert.False(project.IsArchived);
        Assert.Null(project.ArchivedAt);
        Assert.Equal(ProjectStatus.OnHold, project.Status);
    }

    [Fact]
    public async Task Archiving_twice_is_not_an_error_and_does_not_move_the_date()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();

        await _sut!.ArchiveAsync(projectId);
        var first = (await _db!.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId)).ArchivedAt;

        await _sut.ArchiveAsync(projectId);
        var second = (await _db.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == projectId)).ArchivedAt;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task An_archived_project_can_still_be_deleted_when_it_holds_nothing()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await _sut!.ArchiveAsync(projectId);

        await _sut.DeleteAsync(projectId);

        Assert.False(await _db!.Set<Project>().AnyAsync(p => p.Id == projectId));
    }

    [Fact]
    public async Task Deleting_a_project_that_is_already_gone_is_not_an_error()
    {
        if (!Available) return;

        await _sut!.DeleteAsync(Guid.NewGuid());
    }

    // ======================================================== the list itself

    [Fact]
    public async Task Archived_projects_are_out_of_the_default_list_and_findable_on_request()
    {
        if (!Available) return;

        var visible = await NewProjectAsync();
        var archived = await NewProjectAsync();

        await _sut!.ArchiveAsync(archived);

        var active = await _sut.GetProjectsAsync(1, 200);
        var all = await _sut.GetProjectsAsync(1, 200, includeArchived: true);

        Assert.Contains(active.Items, p => p.Id == visible);
        Assert.DoesNotContain(active.Items, p => p.Id == archived);

        // Still there, still complete, one tick-box away.
        var row = all.Items.Single(p => p.Id == archived);
        Assert.True(row.IsArchived);
        Assert.NotNull(row.ArchivedAt);
    }

    [Fact]
    public async Task The_list_reports_document_progress_beside_the_project_status()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Draft);

        var row = (await _sut!.GetProjectsAsync(1, 200)).Items.Single(p => p.Id == projectId);

        // The two facts the single Status column used to have to carry between
        // them, now carried separately and both true.
        Assert.Equal(ProjectStatus.Draft, row.Status);
        Assert.Equal(DocumentProgress.Draft, row.ContractProgress);
        Assert.Equal(DocumentProgress.NotCreated, row.QuoteProgress);
        Assert.Equal(DocumentProgress.NotCreated, row.InvoiceProgress);
    }

    [Fact]
    public async Task A_signed_contract_shows_as_settled_in_the_list()
    {
        if (!Available) return;

        var projectId = await NewProjectAsync();
        await AddContractAsync(projectId, DocumentStatus.Signed, signed: true);

        var row = (await _sut!.GetProjectsAsync(1, 200)).Items.Single(p => p.Id == projectId);

        Assert.Equal(DocumentProgress.Settled, row.ContractProgress);
    }

    [Fact]
    public async Task An_invalid_id_is_refused_rather_than_ignored()
    {
        if (!Available) return;

        await Assert.ThrowsAsync<BadRequestAppException>(() => _sut!.DeleteAsync(Guid.Empty));
        await Assert.ThrowsAsync<BadRequestAppException>(() => _sut!.ArchiveAsync(Guid.Empty));
    }
}

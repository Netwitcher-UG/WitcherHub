using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Runs the assistant actions that take minutes, off the request that asked
    /// for them.
    ///
    /// Reading a supplied document was moved off the request thread when it
    /// started returning HTTP 502; writing the contract and tidying the positions
    /// were left on it. Both call the model, and a model call over a real
    /// contract outlasts what a platform proxy will hold open — so the browser was
    /// shown "the request took too long" while the work was still going, and the
    /// answer landed in a request nobody was listening to.
    ///
    /// Generation then stopped being one model call and became several, which
    /// turned an intermittent failure into a certain one. That is what this
    /// exists to fix.
    ///
    /// The work item captures nothing from the request scope: a fresh scope is
    /// opened inside it, with its own database context and its own services.
    /// Holding the request's context would fail once the request ended and
    /// disposed it — minutes later, with nobody to tell.
    /// </summary>
    public sealed class ContractAiJobService : IContractAiJobs
    {
        private readonly AppDbContext _db;
        private readonly IBackgroundTaskQueue? _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ContractAiJobService> _logger;

        public ContractAiJobService(
            AppDbContext db,
            IServiceScopeFactory scopes,
            ILogger<ContractAiJobService> logger,

            // Optional so a test can construct this without a hosted queue. With
            // none, the work runs inline and the caller's poll simply finds the
            // answer already waiting.
            IBackgroundTaskQueue? queue = null)
        {
            _db = db;
            _scopes = scopes;
            _logger = logger;
            _queue = queue;
        }

        public async Task<ContractAiJobHandle> StartAsync(
            Guid contractId,
            ContractAiJobKind kind,
            object request,
            string? requestKey,
            CancellationToken ct = default)
        {
            var contractExists = await _db.Contracts.AnyAsync(c => c.Id == contractId, ct);

            if (!contractExists)
                return ContractAiJobHandle.Refused("That contract no longer exists.");

            var running = await _db.Set<ContractAiJob>()
                .Where(j => j.ContractId == contractId
                         && j.Kind == kind
                         && j.Status == ContractAiJobStatus.Running)
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefaultAsync(ct);

            if (running is not null)
            {
                // A restart loses whatever the in-process queue was holding, and a
                // row left saying "running" would otherwise refuse every future
                // press for ever.
                if (!running.HasBeenAbandoned)
                    return ContractAiJobHandle.Joined(running.Id);

                running.Status = ContractAiJobStatus.Failed;
                running.FinishedAt = DateTimeOffset.UtcNow;
                running.ErrorIsTransient = true;
                running.Error =
                    "This stopped before it finished, most likely because the application restarted " +
                    "while it was running. Nothing was changed — start it again.";
            }

            var job = new ContractAiJob
            {
                ContractId = contractId,
                Kind = kind,
                Status = ContractAiJobStatus.Running,
                RequestKey = requestKey,
                StartedAt = DateTimeOffset.UtcNow,
                Request = JsonSerializer.SerializeToDocument(request, Json)
            };

            _db.Set<ContractAiJob>().Add(job);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "{Kind} queued for contract {ContractId} as job {JobId}.", kind, contractId, job.Id);

            if (_queue is null)
            {
                await RunAsync(job.Id);
                return ContractAiJobHandle.Started(job.Id);
            }

            // Deliberately not the caller's cancellation token inside the work:
            // that one is tied to the HTTP request, which returns immediately, so
            // passing it would cancel the job the instant the page was answered.
            await _queue.QueueAsync(async _ => await RunAsync(job.Id), ct);

            return ContractAiJobHandle.Started(job.Id);
        }

        public async Task<ContractAiJobState> GetAsync(Guid jobId, CancellationToken ct = default)
        {
            var job = await _db.Set<ContractAiJob>()
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job is null)
            {
                return new ContractAiJobState
                {
                    Status = ContractAiJobStatus.Failed,
                    IsTransientFailure = false,
                    FailureReason = "That request is no longer known. Nothing was changed — try again."
                };
            }

            if (job.HasBeenAbandoned)
            {
                // Reported rather than left spinning. The row is corrected the
                // next time something starts; saying so here keeps this a read.
                return new ContractAiJobState
                {
                    Status = ContractAiJobStatus.Failed,
                    IsTransientFailure = true,
                    FailureReason =
                        "This stopped before it finished, most likely because the application restarted " +
                        "while it was running. Nothing was changed — try again."
                };
            }

            return new ContractAiJobState
            {
                Status = job.Status,
                ResultJson = job.Result?.RootElement.GetRawText(),
                FailureReason = job.Error,

                // As the work judged it. Saying "worth retrying" about a missing
                // API key is what leaves an owner pressing a button that cannot
                // succeed. A row with no stored answer is treated as retryable,
                // which is the safe way to be wrong.
                IsTransientFailure = job.Status == ContractAiJobStatus.Failed &&
                                     job.ErrorIsTransient != false,

                Elapsed = job.Status == ContractAiJobStatus.Running
                    ? DateTimeOffset.UtcNow - job.StartedAt
                    : job.FinishedAt - job.StartedAt
            };
        }

        // ==================================================== the work itself

        private async Task RunAsync(Guid jobId)
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AppDbContext>();

            var job = await db.Set<ContractAiJob>().FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
            {
                _logger.LogWarning("Background job {JobId} vanished before it ran.", jobId);
                return;
            }

            try
            {
                var result = job.Kind switch
                {
                    ContractAiJobKind.Generation => await GenerateAsync(services, job),
                    ContractAiJobKind.Organize => await OrganizeAsync(services, job),
                    _ => Outcome.Fail("That kind of request is not supported.", transient: false)
                };

                Record(job, result);
                await db.SaveChangesAsync();

                _logger.LogInformation(
                    "{Kind} job {JobId} for contract {ContractId} finished: {Status}.",
                    job.Kind, job.Id, job.ContractId, job.Status);
            }
            catch (Exception ex)
            {
                // The handlers record the failures they know about. This is for
                // the ones they do not — a database fault, a bug — which would
                // otherwise leave the row saying "running" until it aged out
                // twenty minutes later with the button stuck the whole time.
                _logger.LogError(ex,
                    "{Kind} job {JobId} for contract {ContractId} failed outside the work itself.",
                    job.Kind, job.Id, job.ContractId);

                await RecordUnexpectedFailureAsync(jobId);
            }
        }

        private static async Task<Outcome> GenerateAsync(IServiceProvider services, ContractAiJob job)
        {
            var drafts = services.GetRequiredService<IContractDraftService>();

            var asked = Read<GenerateJobRequest>(job.Request) ?? new GenerateJobRequest();

            var result = await drafts.GenerateAsync(job.ContractId, new GenerateDraftOptions
            {
                AdditionalInstructions = asked.AdditionalInstructions,
                IdempotencyKey = job.RequestKey,
                Language = string.IsNullOrWhiteSpace(asked.Language) ? "de" : asked.Language!
            });

            if (!result.Succeeded)
                return Outcome.Fail(result.FailureReason ?? "The contract could not be prepared.",
                    result.IsTransientFailure);

            var draft = result.Draft!;

            return Outcome.Ok(new
            {
                version = draft.Version,
                draftId = draft.Id,
                composedWithoutAi = result.ComposedWithoutAi,

                message = result.WasAlreadyPrepared
                    ? $"{draft.KindLabel} version {draft.Version} was already created."
                    : $"{draft.KindLabel} version {draft.Version} created as a draft.",

                // What the version does not account for. Carried through the job
                // because the request that asked for it ended minutes ago.
                reviewNotes = result.ReviewNotes,

                // Said plainly when the assistant could not be used at all and the
                // contract was composed from the record instead.
                notice = result.ComposedWithoutAi ? result.FailureReason : null
            });
        }

        private static async Task<Outcome> OrganizeAsync(IServiceProvider services, ContractAiJob job)
        {
            var organizer = services.GetRequiredService<IAiPositionOrganizer>();
            var positions = services.GetRequiredService<IContractPositions>();

            var asked = Read<OrganizeJobRequest>(job.Request) ?? new OrganizeJobRequest();

            var result = await organizer.OrganizeAsync(new OrganizePositionsRequest
            {
                RoughInput = asked.RoughInput ?? "",
                ExistingPositions = asked.Positions ?? new List<ManualPositionDto>(),
                Currency = string.IsNullOrWhiteSpace(asked.Currency) ? "EUR" : asked.Currency!
            });

            if (!result.Succeeded)
                return Outcome.Fail(result.FailureReason ?? "The assistant could not help.",
                    result.IsTransientFailure);

            // The same shape the synchronous handler returned, so the page reads
            // the answer exactly as it did before.
            return Outcome.Ok(new
            {
                positions = result.Positions,
                totals = positions.CalculateTotals(result.Positions),
                changes = result.Changes.Select(c => new
                {
                    c.PositionTitle, c.Field, c.Before, c.After, kind = c.Kind.ToString()
                }),
                rejected = result.RejectedChanges.Select(c => new
                {
                    c.PositionTitle, c.Field, c.Before, c.After
                }),
                model = result.Model
            });
        }

        private async Task RecordUnexpectedFailureAsync(Guid jobId)
        {
            try
            {
                // A clean context: the one that threw may be in no state to save,
                // since a failed SaveChanges leaves tracked entities as they were.
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await db.Set<ContractAiJob>().FirstOrDefaultAsync(j => j.Id == jobId);
                if (job is null) return;

                job.Status = ContractAiJobStatus.Failed;
                job.FinishedAt = DateTimeOffset.UtcNow;
                job.ErrorIsTransient = true;
                job.Error =
                    "This stopped unexpectedly. Nothing you entered has been lost — try again, or " +
                    "carry on by hand.";

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Nothing further to try. The staleness check is the backstop that
                // gets the job out of "running".
                _logger.LogError(ex, "Could not record the failure of job {JobId}.", jobId);
            }
        }

        private static void Record(ContractAiJob job, Outcome outcome)
        {
            job.FinishedAt = DateTimeOffset.UtcNow;

            if (outcome.Succeeded)
            {
                job.Status = ContractAiJobStatus.Succeeded;
                job.Result = JsonSerializer.SerializeToDocument(outcome.Value, Json);
                job.Error = null;
                job.ErrorIsTransient = null;
                return;
            }

            job.Status = ContractAiJobStatus.Failed;
            job.Error = outcome.Error;
            job.ErrorIsTransient = outcome.Transient;
        }

        // ---------------------------------------------------------------

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private static T? Read<T>(JsonDocument? document) where T : class =>
            document is null ? null : document.Deserialize<T>(Json);



        private readonly record struct Outcome(bool Succeeded, object? Value, string? Error, bool Transient)
        {
            public static Outcome Ok(object value) => new(true, value, null, false);

            public static Outcome Fail(string error, bool transient) =>
                new(false, null, error, transient);
        }
    }
}

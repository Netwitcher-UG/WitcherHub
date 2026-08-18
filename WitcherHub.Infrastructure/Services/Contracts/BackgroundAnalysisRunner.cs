using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Runs a contract analysis on the background queue.
    ///
    /// The work outlives the request that asked for it, so nothing scoped to
    /// that request may be captured here: a fresh scope is opened inside the
    /// queued work item, with its own database context and its own analyser.
    /// Holding the request's context instead would fail once the request ended
    /// and disposed it — usually minutes later, in a place with nobody to tell.
    ///
    /// Every outcome is written to the draft row, because the row is now the
    /// only channel back to the user. A failure that only threw would leave the
    /// page polling a status that never changed.
    /// </summary>
    public sealed class BackgroundAnalysisRunner : IBackgroundAnalysisRunner
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<BackgroundAnalysisRunner> _logger;

        public BackgroundAnalysisRunner(
            IBackgroundTaskQueue queue,
            IServiceScopeFactory scopes,
            ILogger<BackgroundAnalysisRunner> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _logger = logger;
        }

        public ValueTask RunAsync(Guid contractId, int version) =>
            _queue.QueueAsync(async _ =>
            {
                // Deliberately not the caller's cancellation token. That one is
                // tied to the HTTP request, which returns immediately — passing
                // it would cancel the reading the instant the page was answered.
                // The host's shutdown token still stops the queue itself.
                await AnalyseAsync(contractId, version);
            });

        private async Task AnalyseAsync(Guid contractId, int version)
        {
            using var scope = _scopes.CreateScope();

            var drafts = scope.ServiceProvider.GetRequiredService<IContractDraftService>();

            try
            {
                var result = await drafts.AnalyzeAsync(contractId, version);

                _logger.LogInformation(
                    "Background analysis of v{Version} of {ContractId} finished: {Outcome}.",
                    version, contractId, result.Succeeded ? "read" : "failed");
            }
            catch (Exception ex)
            {
                // AnalyzeAsync records the failures it knows about. This is for
                // the ones it does not — a database fault, a bug — which would
                // otherwise leave the draft saying "analysing" until it aged out
                // twenty minutes later.
                _logger.LogError(ex,
                    "Background analysis of v{Version} of {ContractId} failed outside the reading.",
                    version, contractId);

                await RecordUnexpectedFailureAsync(scope.ServiceProvider, contractId, version);
            }
        }

        /// <summary>
        /// Writes the failure straight to the row, with its own context.
        ///
        /// The context that threw may be in no state to save — a failed
        /// SaveChanges leaves tracked entities as they were — so this uses a
        /// clean one and touches only the two columns the page reads.
        /// </summary>
        private async Task RecordUnexpectedFailureAsync(
            IServiceProvider services, Guid contractId, int version)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var draft = await db.Set<ContractDraft>()
                    .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version);

                if (draft is null) return;

                draft.ExtractionStatus = ContractExtractionStatus.Failed;
                draft.ExtractionStartedAt = null;
                draft.ExtractionError =
                    "The analysis stopped unexpectedly. Your document is saved and unchanged — " +
                    "you can try again, or carry on with the original wording.";

                // Nothing here says the fault is permanent, and the message
                // offers a retry, so the flag has to agree with it.
                draft.ExtractionErrorIsTransient = true;

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Nothing further to try. The twenty-minute staleness check is
                // the backstop that gets the draft out of "analysing".
                _logger.LogError(ex,
                    "Could not record the failed analysis of v{Version} of {ContractId}.",
                    version, contractId);
            }
        }
    }
}

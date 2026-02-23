using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Interfaces.BackgroundTasks;

namespace WitcherHub.Infrastructure.Services.BackgroundTasks
{
    public sealed class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly ILogger<QueuedHostedService> _logger;

        public QueuedHostedService(IBackgroundTaskQueue queue, ILogger<QueuedHostedService> logger)
        {
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Func<CancellationToken, ValueTask> workItem;

                try
                {
                    _logger.LogInformation("Dequeued background job");
                    workItem = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {

                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Back[ground task failed.");
                }
            }
        }
    }
}

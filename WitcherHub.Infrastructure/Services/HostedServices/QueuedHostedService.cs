using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;

namespace WitcherHub.Infrastructure.Services.HostedServices
{
    public sealed class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly ILogger<QueuedHostedService> _logger;
        private readonly SemaphoreSlim _throttle;

        public QueuedHostedService(
            IBackgroundTaskQueue queue,
            IOptions<BackgroundTaskOptions> options,
            ILogger<QueuedHostedService> logger)
        {
            _queue = queue;
            _logger = logger;

            var max = Math.Max(1, options.Value.MaxConcurrency);
            _throttle = new SemaphoreSlim(max, max);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var running = new List<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);

                await _throttle.WaitAsync(stoppingToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await workItem(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // ignore
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background work item failed.");
                    }
                    finally
                    {
                        _throttle.Release();
                    }
                }, stoppingToken);

                running.Add(task);
                running.RemoveAll(t => t.IsCompleted);
            }

            await Task.WhenAll(running);
        }
    }
}

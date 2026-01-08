
namespace WitcherHub.Application.Interfaces.BackgroundTasks
{
    public interface IBackgroundTaskQueue
    {
        ValueTask QueueAsync(Func<CancellationToken, ValueTask> workItem, CancellationToken ct = default);
        ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken ct);
    }
}

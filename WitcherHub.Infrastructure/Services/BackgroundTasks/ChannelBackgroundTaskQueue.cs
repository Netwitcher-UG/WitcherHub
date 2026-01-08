using System.Threading.Channels;
using WitcherHub.Application.Interfaces.BackgroundTasks;

namespace WitcherHub.Infrastructure.Services.BackgroundTasks
{
    public sealed class ChannelBackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, ValueTask>> _channel;

        public ChannelBackgroundTaskQueue(int capacity)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            _channel = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(options);
        }

        public ValueTask QueueAsync(Func<CancellationToken, ValueTask> workItem, CancellationToken ct = default)
            => _channel.Writer.WriteAsync(workItem, ct);

        public ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);
    }
}

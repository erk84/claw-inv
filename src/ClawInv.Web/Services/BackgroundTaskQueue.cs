using System.Threading.Channels;

namespace ClawInv.Web.Services;

public sealed class BackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(Func<CancellationToken, Task> workItem)
        => _queue.Writer.WriteAsync(workItem);

    public ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken ct)
        => _queue.Reader.ReadAsync(ct);
}

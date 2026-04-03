using System.Threading.Channels;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Application.Models;

namespace ComfyBridge.Infrastructure.Queue;

public sealed class InMemoryGenerationJobQueue : IGenerationJobQueue
{
    private readonly Channel<GenerationQueueItem> _channel =
        Channel.CreateUnbounded<GenerationQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(GenerationQueueItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public ValueTask<GenerationQueueItem> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
using ComfyBridge.Application.Models;

namespace ComfyBridge.Application.Contracts;

public interface IGenerationJobQueue
{
    ValueTask EnqueueAsync(GenerationQueueItem item, CancellationToken cancellationToken);

    ValueTask<GenerationQueueItem> DequeueAsync(CancellationToken cancellationToken);
}
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Infrastructure.Stores;

public sealed class RedisJobStore : IJobStore
{
    public Task CreateJobAsync(GenerationJob job, CancellationToken cancellationToken) =>
        throw new NotImplementedException("RedisJobStore is scaffolded but not implemented. Register InMemory provider or implement Redis adapter.");

    public Task UpdateJobAsync(GenerationJob job, CancellationToken cancellationToken) =>
        throw new NotImplementedException("RedisJobStore is scaffolded but not implemented. Register InMemory provider or implement Redis adapter.");

    public Task<GenerationJob?> GetJobAsync(string jobId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("RedisJobStore is scaffolded but not implemented. Register InMemory provider or implement Redis adapter.");
}
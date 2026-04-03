using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface IJobStore
{
    Task CreateJobAsync(GenerationJob job, CancellationToken cancellationToken);

    Task UpdateJobAsync(GenerationJob job, CancellationToken cancellationToken);

    Task<GenerationJob?> GetJobAsync(string jobId, CancellationToken cancellationToken);
}
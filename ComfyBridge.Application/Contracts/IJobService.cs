using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface IJobService
{
    Task<GenerationJob> CreateJobAsync(string templateName, string templateVersion, JsonObject inputs, CancellationToken cancellationToken);

    Task<GenerationJob> GetJobAsync(string jobId, CancellationToken cancellationToken);

    Task MarkRunningAsync(string jobId, string externalExecutionId, CancellationToken cancellationToken);

    Task MarkCompletedAsync(string jobId, GenerationResult result, CancellationToken cancellationToken);

    Task MarkFailedAsync(string jobId, string errorMessage, CancellationToken cancellationToken);
}
using System.Collections.Concurrent;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Infrastructure.Stores;

public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<string, GenerationJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public Task CreateJobAsync(GenerationJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs[job.JobId] = Clone(job);
        return Task.CompletedTask;
    }

    public Task UpdateJobAsync(GenerationJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs[job.JobId] = Clone(job);
        return Task.CompletedTask;
    }

    public Task<GenerationJob?> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? Clone(job) : null);
    }

    private static GenerationJob Clone(GenerationJob source) =>
        new()
        {
            JobId = source.JobId,
            TemplateName = source.TemplateName,
            TemplateVersion = source.TemplateVersion,
            Inputs = (System.Text.Json.Nodes.JsonObject)source.Inputs.DeepClone(),
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            ExternalExecutionId = source.ExternalExecutionId,
            ErrorMessage = source.ErrorMessage,
            Result = source.Result is null
                ? null
                : new GenerationResult
                {
                    AssetUrls = source.Result.AssetUrls.ToList(),
                    RawResult = source.Result.RawResult?.DeepClone()
                }
        };
}
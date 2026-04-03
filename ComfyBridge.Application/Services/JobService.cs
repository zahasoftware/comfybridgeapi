using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Enums;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Services;

public sealed class JobService(IJobStore jobStore) : IJobService
{
    public async Task<GenerationJob> CreateJobAsync(string templateName, string templateVersion, JsonObject inputs, CancellationToken cancellationToken)
    {
        var job = new GenerationJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            TemplateName = templateName,
            TemplateVersion = templateVersion,
            Inputs = (JsonObject)inputs.DeepClone(),
            Status = JobStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await jobStore.CreateJobAsync(job, cancellationToken);
        return job;
    }

    public async Task<GenerationJob> GetJobAsync(string jobId, CancellationToken cancellationToken) =>
        await jobStore.GetJobAsync(jobId, cancellationToken) ?? throw new ComfyBridgeException($"Job '{jobId}' was not found.");

    public async Task MarkRunningAsync(string jobId, string externalExecutionId, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        job.Status = JobStatus.Running;
        job.ExternalExecutionId = externalExecutionId;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await jobStore.UpdateJobAsync(job, cancellationToken);
    }

    public async Task MarkCompletedAsync(string jobId, GenerationResult result, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        job.Status = JobStatus.Completed;
        job.Result = result;
        job.ErrorMessage = null;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await jobStore.UpdateJobAsync(job, cancellationToken);
    }

    public async Task MarkFailedAsync(string jobId, string errorMessage, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        job.Status = JobStatus.Failed;
        job.ErrorMessage = errorMessage;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await jobStore.UpdateJobAsync(job, cancellationToken);
    }
}
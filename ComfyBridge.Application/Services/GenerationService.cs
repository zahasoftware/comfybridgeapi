using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Application.Models;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Services;

public sealed class GenerationService(
    ITemplateService templateService,
    IJobService jobService,
    IGenerationJobQueue generationJobQueue) : IGenerationService
{
    public async Task<GenerationJob> StartImageGenerationAsync(string template, JsonObject inputs, CancellationToken cancellationToken)
    {
        var (templateName, templateVersion) = ParseTemplate(template);
        var resolvedTemplate = await templateService.GetTemplateAsync(templateName, templateVersion, cancellationToken);

        var job = await jobService.CreateJobAsync(resolvedTemplate.Name, resolvedTemplate.Version, inputs, cancellationToken);

        await generationJobQueue.EnqueueAsync(new GenerationQueueItem
        {
            JobId = job.JobId,
            TemplateName = resolvedTemplate.Name,
            TemplateVersion = resolvedTemplate.Version,
            Inputs = (JsonObject)inputs.DeepClone()
        }, cancellationToken);

        return job;
    }

    private static (string Name, string? Version) ParseTemplate(string template)
    {
        var parts = template.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => (parts[0], null),
            2 => (parts[0], parts[1]),
            _ => throw new ArgumentException("Template must be in 'name' or 'name:version' format.", nameof(template))
        };
    }
}
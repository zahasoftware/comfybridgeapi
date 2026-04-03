using ComfyBridge.Application.Contracts;
using ComfyBridge.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Api.Workers;

public sealed class GenerationJobWorker(
    IGenerationJobQueue generationJobQueue,
    ITemplateService templateService,
    IWorkflowInjectionService workflowInjectionService,
    IComfyClient comfyClient,
    IJobService jobService,
    IOptions<ComfyUiOptions> comfyOptions,
    ILogger<GenerationJobWorker> logger) : BackgroundService
{
    private readonly TimeSpan _jobTimeout = TimeSpan.FromSeconds(comfyOptions.Value.JobTimeoutSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Generation worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var item = await generationJobQueue.DequeueAsync(stoppingToken);

            try
            {
                var template = await templateService.GetTemplateAsync(item.TemplateName, item.TemplateVersion, stoppingToken);
                var resolvedWorkflow = workflowInjectionService.InjectInputs(template, item.Inputs);

                var externalExecutionId = await comfyClient.SubmitWorkflowAsync(resolvedWorkflow, stoppingToken);
                await jobService.MarkRunningAsync(item.JobId, externalExecutionId, stoppingToken);

                var result = await comfyClient.WaitForResultAsync(externalExecutionId, _jobTimeout, stoppingToken);
                await jobService.MarkCompletedAsync(item.JobId, result, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generation job {JobId} failed", item.JobId);
                await jobService.MarkFailedAsync(item.JobId, ex.Message, stoppingToken);
            }
        }
    }
}
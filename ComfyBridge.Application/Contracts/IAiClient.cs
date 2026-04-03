using ComfyBridge.Application.Models;

namespace ComfyBridge.Application.Contracts;

public interface IAiClient
{
    Task<TemplateDefinition> AnalyzeWorkflowAsync(string workflowJson, CancellationToken cancellationToken);
}
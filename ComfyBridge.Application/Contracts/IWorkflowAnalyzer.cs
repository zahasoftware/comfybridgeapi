using ComfyBridge.Application.Models;

namespace ComfyBridge.Application.Contracts;

public interface IWorkflowAnalyzer
{
    Task<WorkflowAnalysisResult> AnalyzeAsync(string workflowJson, CancellationToken cancellationToken);
}
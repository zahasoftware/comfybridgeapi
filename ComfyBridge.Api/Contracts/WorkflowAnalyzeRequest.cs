namespace ComfyBridge.Api.Contracts;

public sealed class WorkflowAnalyzeRequest
{
    public required string WorkflowJson { get; init; }
}
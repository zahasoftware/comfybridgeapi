using ComfyBridge.Domain.Models;

namespace ComfyBridge.Api.Contracts;

public sealed class WorkflowSaveRequest
{
    public required string Name { get; init; }

    public string Version { get; init; } = "1.0";

    public string Category { get; init; } = string.Empty;

    public required string WorkflowJson { get; init; }

    public required Dictionary<string, string> Inputs { get; init; }

    public required Dictionary<string, WorkflowInputMapping> Mapping { get; init; }
}
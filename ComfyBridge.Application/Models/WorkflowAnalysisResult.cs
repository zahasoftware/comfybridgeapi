using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Models;

public sealed class WorkflowAnalysisResult
{
    public required string WorkflowJson { get; init; }

    public required TemplateDefinition Template { get; init; }

    public required JsonObject ExampleRequest { get; init; }

    public required IReadOnlyCollection<string> DetectedNodeTypes { get; init; }

    public required IReadOnlyCollection<string> Notes { get; init; }
}
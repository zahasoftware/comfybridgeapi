using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Api.Contracts;

public sealed class WorkflowAnalyzeResponse
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }

    public required IReadOnlyDictionary<string, WorkflowInputMapping> Mapping { get; init; }

    public required JsonObject ExampleRequest { get; init; }

    public required IReadOnlyCollection<string> DetectedNodeTypes { get; init; }

    public required IReadOnlyCollection<string> Notes { get; init; }
}
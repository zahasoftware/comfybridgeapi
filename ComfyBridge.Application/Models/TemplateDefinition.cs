using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Models;

public sealed class TemplateDefinition
{
    public required string Name { get; init; }

    public string Version { get; init; } = "1.0";

    public string Category { get; init; } = string.Empty;

    public required Dictionary<string, string> Inputs { get; init; }

    public required Dictionary<string, WorkflowInputMapping> Mapping { get; init; }

    public required JsonObject ExampleRequest { get; init; }
}
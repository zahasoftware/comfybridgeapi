using System.Text.Json.Nodes;

namespace ComfyBridge.Domain.Models;

public sealed class WorkflowTemplate
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required Dictionary<string, string> Inputs { get; init; }

    public required Dictionary<string, WorkflowInputMapping> Mapping { get; init; }

    public required JsonNode Workflow { get; init; }

    public JsonObject? ExampleRequest { get; set; }

    public string TemplateKey => $"{Name}:{Version}";
}
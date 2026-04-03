using System.Text.Json.Nodes;

namespace ComfyBridge.Application.Models;

public sealed class GenerationQueueItem
{
    public required string JobId { get; init; }

    public required string TemplateName { get; init; }

    public required string TemplateVersion { get; init; }

    public required JsonObject Inputs { get; init; }
}
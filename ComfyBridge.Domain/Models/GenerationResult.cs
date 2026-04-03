using System.Text.Json.Nodes;

namespace ComfyBridge.Domain.Models;

public sealed class GenerationResult
{
    public required IReadOnlyList<string> AssetUrls { get; init; }

    public JsonNode? RawResult { get; init; }
}
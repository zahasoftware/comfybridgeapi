using System.Text.Json.Nodes;

namespace ComfyBridge.Api.Contracts;

public sealed class GenerateImageRequest
{
    public required string Template { get; init; }

    public required JsonObject Inputs { get; init; }
}
using System.Text.Json.Nodes;

namespace ComfyBridge.Api.Contracts;

public sealed class JobResponse
{
    public required string JobId { get; init; }

    public required string Template { get; init; }

    public required string Version { get; init; }

    public required string Status { get; init; }

    public string? ExternalExecutionId { get; init; }

    public JsonNode? Result { get; init; }

    public IReadOnlyList<string>? AssetUrls { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
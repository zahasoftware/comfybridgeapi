namespace ComfyBridge.Api.Contracts;

public sealed class RunResponse
{
    public required string JobId { get; init; }

    public required string Status { get; init; }

    /// <summary>Poll this URL to check job progress.</summary>
    public required string StatusUrl { get; init; }

    public required string Template { get; init; }

    public required string Version { get; init; }

    public required string Category { get; init; }
}

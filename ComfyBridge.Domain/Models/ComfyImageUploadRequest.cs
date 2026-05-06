namespace ComfyBridge.Domain.Models;

public sealed class ComfyImageUploadRequest
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public string Type { get; init; } = "input";

    public string? Subfolder { get; init; }

    public bool Overwrite { get; init; } = true;
}

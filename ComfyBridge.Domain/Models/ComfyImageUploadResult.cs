namespace ComfyBridge.Domain.Models;

public sealed class ComfyImageUploadResult
{
    public required string ComfyFileName { get; init; }

    public string? Subfolder { get; init; }

    public string Type { get; init; } = "input";

    public string? ViewUrl { get; init; }
}

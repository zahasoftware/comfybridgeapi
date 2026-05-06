namespace ComfyBridge.Api.Contracts;

public sealed class ImageUploadResponse
{
    public required string ComfyFileName { get; init; }

    public string? Subfolder { get; init; }

    public required string Type { get; init; }

    public string? ViewUrl { get; init; }
}

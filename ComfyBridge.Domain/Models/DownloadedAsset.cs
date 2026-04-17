namespace ComfyBridge.Domain.Models;

public sealed class DownloadedAsset
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}
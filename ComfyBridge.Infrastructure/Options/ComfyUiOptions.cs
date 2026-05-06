namespace ComfyBridge.Infrastructure.Options;

public sealed class ComfyUiOptions
{
    public const string SectionName = "ComfyUi";

    public string BaseUrl { get; init; } = "http://127.0.0.1:8188";

    public int PollIntervalMs { get; init; } = 1000;

    public int JobTimeoutSeconds { get; init; } = 600;

    public int UploadMaxFileSizeBytes { get; init; } = 10_485_760;

    public string[] AllowedUploadExtensions { get; init; } = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
}
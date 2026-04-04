namespace ComfyBridge.Api.Contracts;

public sealed class TemplateResponse
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public string Category { get; init; } = string.Empty;

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }
}
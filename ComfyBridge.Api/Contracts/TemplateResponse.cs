namespace ComfyBridge.Api.Contracts;

public sealed class TemplateResponse
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }
}
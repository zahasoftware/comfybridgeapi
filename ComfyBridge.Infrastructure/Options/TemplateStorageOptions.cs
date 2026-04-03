namespace ComfyBridge.Infrastructure.Options;

public sealed class TemplateStorageOptions
{
    public const string SectionName = "TemplateStorage";

    public string TemplatesPath { get; init; } = "Templates";
}
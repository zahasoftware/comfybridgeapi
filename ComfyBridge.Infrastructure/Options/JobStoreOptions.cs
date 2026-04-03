namespace ComfyBridge.Infrastructure.Options;

public sealed class JobStoreOptions
{
    public const string SectionName = "JobStore";

    public string Provider { get; init; } = "InMemory";
}
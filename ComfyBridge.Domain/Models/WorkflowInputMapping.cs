namespace ComfyBridge.Domain.Models;

public sealed class WorkflowInputMapping
{
    public string? JsonPointer { get; init; }

    public string? NodeId { get; init; }

    public string? NodeClass { get; init; }

    public int NodeIndex { get; init; } = 0;

    public string? Field { get; init; }
}
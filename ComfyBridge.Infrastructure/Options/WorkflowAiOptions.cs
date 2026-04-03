namespace ComfyBridge.Infrastructure.Options;

public sealed class WorkflowAiOptions
{
    public const string SectionName = "WorkflowAi";

    public string Provider { get; init; } = "Ollama";

    public string Endpoint { get; init; } = "http://127.0.0.1:11434";

    public string Model { get; init; } = "llama3.1";

    public string? ApiKey { get; init; }

    public bool AllowHeuristicFallback { get; init; } = true;

    public int MaxTemplateValidationAttempts { get; init; } = 3;
}
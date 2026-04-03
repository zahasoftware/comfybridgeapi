using System.Text.Json.Nodes;
using ComfyBridge.Domain.Enums;

namespace ComfyBridge.Domain.Models;

public sealed class GenerationJob
{
    public required string JobId { get; init; }

    public required string TemplateName { get; init; }

    public required string TemplateVersion { get; init; }

    public required JsonObject Inputs { get; init; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? ExternalExecutionId { get; set; }

    public string? ErrorMessage { get; set; }

    public GenerationResult? Result { get; set; }
}
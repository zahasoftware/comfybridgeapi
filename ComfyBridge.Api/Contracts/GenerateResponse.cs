namespace ComfyBridge.Api.Contracts;

public sealed class GenerateResponse
{
    public required string JobId { get; init; }

    public required string Status { get; init; }
}
using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface IGenerationService
{
    Task<GenerationJob> StartImageGenerationAsync(string template, JsonObject inputs, CancellationToken cancellationToken);
}
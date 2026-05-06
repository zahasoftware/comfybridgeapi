using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface IComfyClient
{
    Task<string> SubmitWorkflowAsync(JsonNode workflow, CancellationToken cancellationToken);

    Task<ComfyImageUploadResult> UploadImageAsync(Stream fileStream, ComfyImageUploadRequest request, CancellationToken cancellationToken);

    Task<GenerationResult> WaitForResultAsync(string externalExecutionId, TimeSpan timeout, CancellationToken cancellationToken);

    Task<DownloadedAsset> DownloadAssetAsync(string assetUrl, CancellationToken cancellationToken);
}
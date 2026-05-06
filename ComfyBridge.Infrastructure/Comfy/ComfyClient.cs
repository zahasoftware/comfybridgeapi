using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using ComfyBridge.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Infrastructure.Comfy;

public sealed class ComfyClient(
    HttpClient httpClient,
    IOptions<ComfyUiOptions> comfyOptions,
    ILogger<ComfyClient> logger) : IComfyClient
{
    private readonly ComfyUiOptions _options = comfyOptions.Value;

    public async Task<string> SubmitWorkflowAsync(JsonNode workflow, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["prompt"] = workflow.DeepClone()
        };

        using var response = await httpClient.PostAsJsonAsync("prompt", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ExternalServiceException($"ComfyUI /prompt failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var promptId = json?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new ExternalServiceException("ComfyUI did not return a prompt_id.");
        }

        return promptId;
    }

    public async Task<ComfyImageUploadResult> UploadImageAsync(
        Stream fileStream,
        ComfyImageUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (fileStream is null)
        {
            throw new InputValidationException("Image stream is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new InputValidationException("Image file name is required.");
        }

        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        }

        form.Add(streamContent, "image", request.FileName);
        form.Add(new StringContent(string.IsNullOrWhiteSpace(request.Type) ? "input" : request.Type), "type");
        form.Add(new StringContent(request.Overwrite ? "true" : "false"), "overwrite");

        if (!string.IsNullOrWhiteSpace(request.Subfolder))
        {
            form.Add(new StringContent(request.Subfolder), "subfolder");
        }

        using var response = await httpClient.PostAsync("upload/image", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ExternalServiceException($"ComfyUI /upload/image failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken)
            ?? throw new ExternalServiceException("ComfyUI image upload returned an empty response.");

        var comfyFileName = json["name"]?.GetValue<string>()
            ?? json["filename"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(comfyFileName))
        {
            throw new ExternalServiceException("ComfyUI image upload did not return the stored file name.");
        }

        var subfolder = json["subfolder"]?.GetValue<string>();
        var type = json["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
        {
            type = string.IsNullOrWhiteSpace(request.Type) ? "input" : request.Type;
        }

        return new ComfyImageUploadResult
        {
            ComfyFileName = comfyFileName,
            Subfolder = subfolder,
            Type = type,
            ViewUrl = BuildViewUrl(comfyFileName, subfolder, type)
        };
    }

    public async Task<GenerationResult> WaitForResultAsync(string externalExecutionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                using var response = await httpClient.GetAsync($"history/{Uri.EscapeDataString(externalExecutionId)}", timeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var history = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: timeoutCts.Token);
                    var completed = TryBuildResultFromHistory(externalExecutionId, history);
                    if (completed is not null)
                    {
                        return completed;
                    }
                }

                await Task.Delay(_options.PollIntervalMs, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("ComfyUI timed out waiting for prompt {PromptId} after {TimeoutSeconds}s", externalExecutionId, timeout.TotalSeconds);
            throw new TimeoutException($"Timed out waiting for ComfyUI prompt '{externalExecutionId}' after {timeout.TotalSeconds:0} seconds.");
        }

        logger.LogWarning("ComfyUI timed out waiting for prompt {PromptId}", externalExecutionId);
        throw new TimeoutException($"Timed out waiting for ComfyUI prompt '{externalExecutionId}' after {timeout.TotalSeconds:0} seconds.");
    }

    public async Task<DownloadedAsset> DownloadAssetAsync(string assetUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            throw new InputValidationException("Asset URL is required.");
        }

        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri))
        {
            throw new InputValidationException("Asset URL is invalid.");
        }

        if (httpClient.BaseAddress is not null &&
            !string.Equals(assetUri.Host, httpClient.BaseAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InputValidationException("Asset URL host does not match configured ComfyUI host.");
        }

        using var response = await httpClient.GetAsync(assetUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ComfyBridgeException("Generated asset was not found in ComfyUI output storage.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ExternalServiceException($"ComfyUI asset download failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = ResolveFileNameFromAssetUrl(assetUri) ?? "output.bin";

        return new DownloadedAsset
        {
            FileName = fileName,
            ContentType = contentType,
            Content = content
        };
    }

    private GenerationResult? TryBuildResultFromHistory(string promptId, JsonObject? history)
    {
        var promptNode = history?[promptId] as JsonObject;
        var outputs = promptNode?["outputs"] as JsonObject;
        if (outputs is null)
        {
            return null;
        }

        var urls = new List<string>();
        foreach (var (_, outputNode) in outputs)
        {
            if (outputNode is not JsonObject outputObject)
            {
                continue;
            }

            foreach (var key in new[] { "images", "videos", "gifs" })
            {
                if (outputObject[key] is not JsonArray assets)
                {
                    continue;
                }

                foreach (var assetNode in assets)
                {
                    if (assetNode is not JsonObject asset)
                    {
                        continue;
                    }

                    var filename = asset["filename"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(filename))
                    {
                        continue;
                    }

                    var subfolder = asset["subfolder"]?.GetValue<string>();
                    var type = asset["type"]?.GetValue<string>();
                    var url = BuildViewUrl(filename, subfolder, type);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        urls.Add(url);
                    }
                }
            }
        }

        return new GenerationResult
        {
            AssetUrls = urls,
            RawResult = promptNode?.DeepClone()
        };
    }

    private string? BuildViewUrl(string filename, string? subfolder, string? type)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var query = new List<string>
        {
            $"filename={Uri.EscapeDataString(filename)}"
        };

        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            query.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query.Add($"type={Uri.EscapeDataString(type)}");
        }

        return $"{httpClient.BaseAddress}view?{string.Join("&", query)}";
    }

    private static string? ResolveFileNameFromAssetUrl(Uri assetUri)
    {
        var query = assetUri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            if (!string.Equals(kv[0], "filename", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decoded = Uri.UnescapeDataString(kv[1]);
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }

        return null;
    }
}
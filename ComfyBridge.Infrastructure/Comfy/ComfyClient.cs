using System.Net.Http.Json;
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

    public async Task<GenerationResult> WaitForResultAsync(string externalExecutionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

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

        logger.LogWarning("ComfyUI timed out waiting for prompt {PromptId}", externalExecutionId);
        throw new TimeoutException($"Timed out waiting for ComfyUI prompt '{externalExecutionId}'.");
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

            if (outputObject["images"] is not JsonArray images)
            {
                continue;
            }

            foreach (var imageNode in images)
            {
                if (imageNode is not JsonObject image)
                {
                    continue;
                }

                var filename = image["filename"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                var query = new List<string>
                {
                    $"filename={Uri.EscapeDataString(filename)}"
                };

                var subfolder = image["subfolder"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(subfolder))
                {
                    query.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
                }

                var type = image["type"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(type))
                {
                    query.Add($"type={Uri.EscapeDataString(type)}");
                }

                urls.Add($"{httpClient.BaseAddress}view?{string.Join("&", query)}");
            }
        }

        return new GenerationResult
        {
            AssetUrls = urls,
            RawResult = promptNode?.DeepClone()
        };
    }
}
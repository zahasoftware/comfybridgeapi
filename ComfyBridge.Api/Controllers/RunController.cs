using System.Text.Json.Nodes;
using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

/// <summary>
/// Exposes a simple, category-namespaced run endpoint for every saved workflow template.
///
///   POST /api/v1/{category}/{name}
///   Body: only the fields declared in the template's input mapping.
///
/// Category examples: text2image, text2video, image2image, image2video, inpainting, upscale, general
/// </summary>
[ApiController]
[Route("api/v1/{category}/{name}")]
public sealed class RunController(
    ITemplateService templateService,
    IGenerationService generationService) : ControllerBase
{
    /// <summary>
    /// Submit a generation job for the given workflow.
    /// Returns 202 Accepted with a Job ID. Poll /api/v1/jobs/{jobId} for status.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RunResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Run(
        [FromRoute] string category,
        [FromRoute] string name,
        [FromBody] JsonObject? body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            throw new InputValidationException("Request body is required and must be a JSON object.");
        }

        // Find the latest version of this template.
        var template = await templateService.GetTemplateAsync(name, version: null, cancellationToken);

        // Build a safe inputs object with only declared mapped fields.
        // Unknown keys are rejected; missing keys are rejected.
        var unknownKeys = body.Select(p => p.Key)
            .Where(k => !template.Inputs.ContainsKey(k))
            .ToArray();

        if (unknownKeys.Length > 0)
        {
            throw new InputValidationException(
                $"Unknown input field(s): {string.Join(", ", unknownKeys)}. " +
                $"Accepted fields: {string.Join(", ", template.Inputs.Keys)}.");
        }

        var providedKeys = template.Inputs.Keys
            .Where(body.ContainsKey)
            .ToArray();

        if (providedKeys.Length == 0)
        {
            throw new InputValidationException(
                "At least one input field is required. " +
                $"Accepted fields: {string.Join(", ", template.Inputs.Keys)}.");
        }

        var defaultValues = GetWorkflowDefaultValues(template);

        var keysMissingDefault = template.Inputs.Keys
            .Where(k => !body.ContainsKey(k) && !defaultValues.ContainsKey(k))
            .ToArray();

        if (keysMissingDefault.Length > 0)
        {
            throw new InputValidationException(
                "Some required fields were not provided and do not have defaults in the workflow: " +
                $"{string.Join(", ", keysMissingDefault)}.");
        }

        var safeInputs = new JsonObject();
        foreach (var key in template.Inputs.Keys)
        {
            if (body.TryGetPropertyValue(key, out var providedValue) && providedValue is not null)
            {
                safeInputs[key] = providedValue.DeepClone();
                continue;
            }

            safeInputs[key] = defaultValues[key].DeepClone();
        }

        var job = await generationService.StartImageGenerationAsync(
            $"{template.Name}:{template.Version}",
            safeInputs,
            cancellationToken);

        return Accepted(new RunResponse
        {
            JobId = job.JobId,
            Status = job.Status.ToString(),
            StatusUrl = $"/api/v1/jobs/{job.JobId}",
            Template = template.Name,
            Version = template.Version,
            Category = string.IsNullOrWhiteSpace(template.Category) ? category : template.Category
        });
    }

    private static Dictionary<string, JsonNode> GetWorkflowDefaultValues(WorkflowTemplate template)
    {
        var defaults = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, _) in template.Inputs)
        {
            if (!template.Mapping.TryGetValue(key, out var mapping))
            {
                continue;
            }

            var val = ReadWorkflowDefaultValue(template.Workflow, mapping);
            if (val is not null)
            {
                defaults[key] = val;
            }
        }

        return defaults;
    }

    private static JsonNode? ReadWorkflowDefaultValue(JsonNode workflow, WorkflowInputMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.JsonPointer))
        {
            return ReadByJsonPointer(workflow, mapping.JsonPointer!);
        }

        if (!string.IsNullOrWhiteSpace(mapping.NodeClass) && !string.IsNullOrWhiteSpace(mapping.Field))
        {
            return ReadByNodeClass(workflow, mapping.NodeClass!, mapping.NodeIndex, mapping.Field!);
        }

        if (!string.IsNullOrWhiteSpace(mapping.NodeId) && !string.IsNullOrWhiteSpace(mapping.Field))
        {
            return ReadByNodeId(workflow, mapping.NodeId!, mapping.Field!);
        }

        return null;
    }

    private static JsonNode? ReadByJsonPointer(JsonNode node, string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer) || pointer[0] != '/')
        {
            return null;
        }

        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();

        JsonNode? current = node;
        foreach (var segment in segments)
        {
            current = current switch
            {
                JsonObject obj => obj[segment],
                JsonArray arr when int.TryParse(segment, out var idx) && idx >= 0 && idx < arr.Count => arr[idx],
                _ => null
            };

            if (current is null)
            {
                return null;
            }
        }

        return current.DeepClone();
    }

    private static JsonNode? ReadByNodeClass(JsonNode workflow, string nodeClass, int nodeIndex, string field)
    {
        if (workflow is not JsonObject root)
        {
            return null;
        }

        var candidates = EnumerateWorkflowNodes(root)
            .Where(n => string.Equals(n["class_type"]?.GetValue<string>(), nodeClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= nodeIndex)
        {
            return null;
        }

        return ReadNodeInputField(candidates[nodeIndex], field);
    }

    private static JsonNode? ReadByNodeId(JsonNode workflow, string nodeId, string field)
    {
        if (workflow is not JsonObject root)
        {
            return null;
        }

        if (root[nodeId] is JsonObject directNode)
        {
            return ReadNodeInputField(directNode, field);
        }

        foreach (var node in EnumerateWorkflowNodes(root))
        {
            if (string.Equals(node["id"]?.ToString(), nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return ReadNodeInputField(node, field);
            }
        }

        return null;
    }

    private static JsonNode? ReadNodeInputField(JsonObject node, string field)
    {
        if (node["inputs"] is not JsonObject inputs)
        {
            return null;
        }

        return inputs.TryGetPropertyValue(field, out var val) && val is not null
            ? val.DeepClone()
            : null;
    }

    private static IEnumerable<JsonObject> EnumerateWorkflowNodes(JsonObject root)
    {
        foreach (var (_, nodeValue) in root)
        {
            if (nodeValue is JsonObject nodeObject && nodeObject.ContainsKey("class_type"))
            {
                yield return nodeObject;
            }
        }

        if (root["nodes"] is not JsonArray nodes)
        {
            yield break;
        }

        foreach (var item in nodes)
        {
            if (item is JsonObject nodeObject)
            {
                yield return nodeObject;
            }
        }
    }
}

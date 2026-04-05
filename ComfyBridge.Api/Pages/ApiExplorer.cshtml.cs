using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages;

public sealed class ApiExplorerModel(ITemplateService templateService) : PageModel
{
    public IReadOnlyCollection<WorkflowTemplate> Templates { get; private set; } = [];

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Templates = await templateService.GetTemplatesAsync(cancellationToken);
        BaseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
    }

    /// <summary>
    /// Returns the route path for a template's dynamic run endpoint.
    /// Category falls back to "general" when not set.
    /// </summary>
    public static string GetEndpointPath(WorkflowTemplate template)
    {
        var category = string.IsNullOrWhiteSpace(template.Category) ? "general" : template.Category;
        return $"/api/v1/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(template.Name)}";
    }

    /// <summary>
    /// Returns a human-readable description of what a mapped input connects to inside the workflow.
    /// </summary>
    public static string GetFieldDescription(WorkflowTemplate template, string key)
    {
        if (!template.Mapping.TryGetValue(key, out var map))
        {
            return string.Empty;
        }

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(map.NodeClass))
        {
            parts.Add(map.NodeClass);
        }

        if (!string.IsNullOrWhiteSpace(map.Field))
        {
            parts.Add($"field \"{map.Field}\"");
        }

        return parts.Count > 0 ? string.Join(" → ", parts) : string.Empty;
    }

    /// <summary>
    /// Extracts the current (default) value for each input directly from the raw workflow JSON
    /// using the configured mapping for each field.
    /// </summary>
    public static Dictionary<string, string> GetWorkflowDefaultValues(WorkflowTemplate template)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, _) in template.Inputs)
        {
            if (!template.Mapping.TryGetValue(key, out var mapping))
                continue;
            var val = ReadWorkflowDefaultValue(template.Workflow, mapping);
            if (val is not null)
                defaults[key] = val;
        }

        return defaults;
    }

    private static string? ReadWorkflowDefaultValue(JsonNode workflow, WorkflowInputMapping mapping)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(mapping.JsonPointer))
                return ReadByJsonPointer(workflow, mapping.JsonPointer!);

            if (!string.IsNullOrWhiteSpace(mapping.NodeClass) && !string.IsNullOrWhiteSpace(mapping.Field))
                return ReadByNodeClass(workflow, mapping.NodeClass!, mapping.NodeIndex, mapping.Field!);

            if (!string.IsNullOrWhiteSpace(mapping.NodeId) && !string.IsNullOrWhiteSpace(mapping.Field))
                return ReadByNodeId(workflow, mapping.NodeId!, mapping.Field!);
        }
        catch { /* ignore – fall through to no default */ }

        return null;
    }

    private static string? ReadByJsonPointer(JsonNode node, string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer) || pointer[0] != '/') return null;

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
            if (current is null) return null;
        }

        return current is null ? null : JsonNodeToString(current);
    }

    private static string? ReadByNodeClass(JsonNode workflow, string nodeClass, int nodeIndex, string field)
    {
        if (workflow is not JsonObject root) return null;

        var candidates = EnumerateWorkflowNodes(root)
            .Where(n => string.Equals(n["class_type"]?.GetValue<string>(), nodeClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= nodeIndex) return null;
        return ReadNodeInputField(candidates[nodeIndex], field);
    }

    private static string? ReadByNodeId(JsonNode workflow, string nodeId, string field)
    {
        if (workflow is not JsonObject root) return null;

        if (root[nodeId] is JsonObject directNode)
            return ReadNodeInputField(directNode, field);

        foreach (var node in EnumerateWorkflowNodes(root))
        {
            if (string.Equals(node["id"]?.ToString(), nodeId, StringComparison.OrdinalIgnoreCase))
                return ReadNodeInputField(node, field);
        }

        return null;
    }

    private static string? ReadNodeInputField(JsonObject node, string field)
    {
        if (node["inputs"] is not JsonObject inputs) return null;
        return inputs.TryGetPropertyValue(field, out var val) && val is not null ? JsonNodeToString(val) : null;
    }

    private static IEnumerable<JsonObject> EnumerateWorkflowNodes(JsonObject root)
    {
        foreach (var (_, nodeValue) in root)
        {
            if (nodeValue is JsonObject nodeObject && nodeObject.ContainsKey("class_type"))
                yield return nodeObject;
        }

        if (root["nodes"] is JsonArray nodes)
        {
            foreach (var item in nodes)
            {
                if (item is JsonObject nodeObject)
                    yield return nodeObject;
            }
        }
    }

    private static string JsonNodeToString(JsonNode node)
    {
        if (node is JsonValue val)
        {
            var raw = val.ToJsonString();
            // Strip surrounding quotes from JSON strings
            if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                return raw[1..^1];
            return raw;
        }
        return node.ToJsonString();
    }

    /// <summary>
    /// Builds a ready-to-copy curl command for the given template endpoint.
    /// </summary>
    public static string BuildCurlExample(string baseUrl, string endpointPath, WorkflowTemplate template)
    {
        var sample = new JsonObject();
        foreach (var (key, type) in template.Inputs)
        {
            sample[key] = type.ToLowerInvariant() switch
            {
                "int" or "integer" => JsonValue.Create(1),
                "float" or "double" or "number" => JsonValue.Create(1.0),
                "bool" or "boolean" => JsonValue.Create(false),
                _ => JsonValue.Create($"your-{key}-here")
            };
        }

        var json = sample.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var safeJson = json.Replace("'", "\\'");
        return $"curl -X POST '{baseUrl}{endpointPath}' \\\n  -H 'Content-Type: application/json' \\\n  -d '{safeJson}'";
    }
}

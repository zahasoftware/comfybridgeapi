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

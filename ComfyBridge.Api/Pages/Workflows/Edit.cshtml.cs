using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages.Workflows;

public sealed class EditModel(
    ITemplateService templateService,
    IWorkflowAnalyzer analyzer) : PageModel
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string Version { get; set; } = "1.0";

    [BindProperty]
    public string Category { get; set; } = string.Empty;

    [BindProperty]
    public string WorkflowJson { get; set; } = string.Empty;

    [BindProperty]
    public string OriginalKey { get; set; } = string.Empty;

    [BindProperty]
    public List<EditableInput> Inputs { get; set; } = [];

    public IReadOnlyCollection<string> DetectedNodeTypes { get; private set; } = [];

    public string MappingJson { get; private set; } = "{\n  \"inputs\": {},\n  \"mapping\": {}\n}";

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var key = Uri.UnescapeDataString(id);
        var split = key.Split(':', 2);
        if (split.Length != 2)
        {
            return NotFound("Template identifier is invalid.");
        }

        WorkflowTemplate template;
        try
        {
            template = await templateService.GetTemplateAsync(split[0], split[1], cancellationToken);
        }
        catch (Exception)
        {
            return NotFound("Template not found.");
        }

        OriginalKey = id;
        Name = template.Name;
        Version = template.Version;
        Category = template.Category;
        WorkflowJson = template.Workflow.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        DetectedNodeTypes = ExtractNodeTypes(template.Workflow);
        Inputs = template.Mapping
            .Select(pair => new EditableInput
            {
                InputName = pair.Key,
                InputType = template.Inputs.TryGetValue(pair.Key, out var type) ? type : "string",
                NodeId = pair.Value.NodeId,
                NodeClass = pair.Value.NodeClass,
                NodeIndex = pair.Value.NodeIndex,
                Field = pair.Value.Field
            })
            .ToList();
        RefreshMappingJson();

        return Page();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(WorkflowJson))
        {
            ErrorMessage = "Workflow JSON is required to regenerate mapping.";
            return Page();
        }

        try
        {
            var result = await analyzer.AnalyzeAsync(WorkflowJson, cancellationToken);

            var workflowNode = JsonNode.Parse(WorkflowJson)!;
            DetectedNodeTypes = ExtractNodeTypes(workflowNode);

            Inputs = result.Template.Mapping
                .Select(pair => new EditableInput
                {
                    InputName = pair.Key,
                    InputType = result.Template.Inputs.TryGetValue(pair.Key, out var type) ? type : "string",
                    NodeId = pair.Value.NodeId,
                    NodeClass = pair.Value.NodeClass,
                    NodeIndex = pair.Value.NodeIndex,
                    Field = pair.Value.Field
                })
                .ToList();
            RefreshMappingJson();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to regenerate mapping: {ex.Message}";
            RehydrateDerivedState();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRegenerateAndSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Template name is required.";
            RehydrateDerivedState();
            return Page();
        }

        try
        {
            var result = await analyzer.AnalyzeAsync(WorkflowJson, cancellationToken);
            Inputs = result.Template.Mapping
                .Select(pair => new EditableInput
                {
                    InputName = pair.Key,
                    InputType = result.Template.Inputs.TryGetValue(pair.Key, out var type) ? type : "string",
                    NodeId = pair.Value.NodeId,
                    NodeClass = pair.Value.NodeClass,
                    NodeIndex = pair.Value.NodeIndex,
                    Field = pair.Value.Field
                })
                .ToList();

            var template = BuildTemplateFromForm();
            await templateService.SaveTemplateAsync(template, cancellationToken);

            DetectedNodeTypes = ExtractNodeTypes(template.Workflow);
            RefreshMappingJson();
            StatusMessage = "Mapping regenerated and saved.";
            return Page();
        }
        catch (Exception ex) when (ex is JsonException or InputValidationException)
        {
            ErrorMessage = $"Failed to regenerate and save mapping: {ex.Message}";
            RehydrateDerivedState();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Template name is required.";
            RehydrateDerivedState();
            return Page();
        }

        try
        {
            var template = BuildTemplateFromForm();
            await templateService.SaveTemplateAsync(template, cancellationToken);
            StatusMessage = "Template saved.";
        }
        catch (Exception ex) when (ex is JsonException or InputValidationException)
        {
            ErrorMessage = ex.Message;
            RehydrateDerivedState();
            return Page();
        }

        return Redirect("/workflows");
    }

    public sealed class EditableInput
    {
        public string InputName { get; init; } = string.Empty;

        public string InputType { get; init; } = "string";

        public string? NodeId { get; init; }

        public string? NodeClass { get; init; }

        public int NodeIndex { get; init; }

        public string? Field { get; init; }
    }

    private WorkflowTemplate BuildTemplateFromForm()
    {
        var workflowNode = JsonNode.Parse(WorkflowJson) ?? throw new InputValidationException("Workflow JSON is required.");
        using var doc = JsonDocument.Parse(WorkflowJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("Workflow JSON must be an object.");
        }

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mapping = new Dictionary<string, WorkflowInputMapping>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Inputs)
        {
            if (string.IsNullOrWhiteSpace(item.InputName))
            {
                continue;
            }

            inputs[item.InputName] = string.IsNullOrWhiteSpace(item.InputType) ? "string" : item.InputType;
            mapping[item.InputName] = new WorkflowInputMapping
            {
                NodeId = item.NodeId,
                NodeClass = item.NodeClass,
                NodeIndex = item.NodeIndex,
                Field = item.Field
            };
        }

        return new WorkflowTemplate
        {
            Name = Name,
            Version = Version,
            Category = Category,
            Workflow = workflowNode,
            Inputs = inputs,
            Mapping = mapping
        };
    }

    private void RehydrateDerivedState()
    {
        RefreshMappingJson();

        if (!string.IsNullOrWhiteSpace(WorkflowJson))
        {
            try
            {
                var node = JsonNode.Parse(WorkflowJson);
                if (node is not null)
                {
                    DetectedNodeTypes = ExtractNodeTypes(node);
                }
            }
            catch { /* ignore */ }
        }
    }

    private void RefreshMappingJson()
    {
        var inputs = new JsonObject();
        var mapping = new JsonObject();

        foreach (var item in Inputs.Where(item => !string.IsNullOrWhiteSpace(item.InputName)))
        {
            inputs[item.InputName] = string.IsNullOrWhiteSpace(item.InputType) ? "string" : item.InputType;
            mapping[item.InputName] = new JsonObject
            {
                ["nodeId"] = item.NodeId,
                ["nodeClass"] = item.NodeClass,
                ["nodeIndex"] = item.NodeIndex,
                ["field"] = item.Field
            };
        }

        MappingJson = new JsonObject
        {
            ["inputs"] = inputs,
            ["mapping"] = mapping
        }.ToJsonString(PrettyJsonOptions);
    }

    private static IReadOnlyCollection<string> ExtractNodeTypes(JsonNode workflow)
    {
        if (workflow is not JsonObject obj)
        {
            return [];
        }

        var rootNodes = obj
            .Select(pair => pair.Value)
            .OfType<JsonObject>();

        var nestedNodes = (obj["nodes"] as JsonArray)?
            .OfType<JsonObject>()
            ?? [];

        var values = rootNodes
            .Concat(nestedNodes)
            .Select(node => node["class_type"]?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values!;
    }
}

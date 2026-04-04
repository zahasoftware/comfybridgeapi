using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages.Workflows;

public sealed class DetailsModel(ITemplateService templateService, IWorkflowDraftStore draftStore) : PageModel
{
    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string Version { get; set; } = "1.0";

    [BindProperty]
    public string Category { get; set; } = string.Empty;

    [BindProperty]
    public string WorkflowJson { get; set; } = string.Empty;

    [BindProperty]
    public string DraftId { get; set; } = string.Empty;

    [BindProperty]
    public bool IsDraft { get; set; }

    [BindProperty]
    public List<EditableInput> Inputs { get; set; } = [];

    public IReadOnlyCollection<string> DetectedNodeTypes { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, bool draft, CancellationToken cancellationToken)
    {
        if (draft)
        {
            if (!draftStore.TryGetDraft(id, out var draftResult) || draftResult is null)
            {
                return NotFound("Draft not found.");
            }

            DraftId = id;
            IsDraft = true;
            Name = draftResult.Template.Name;
            Version = draftResult.Template.Version;
            Category = draftResult.Template.Category;
            WorkflowJson = draftResult.WorkflowJson;
            DetectedNodeTypes = draftResult.DetectedNodeTypes;
            Inputs = draftResult.Template.Mapping
                .Select(pair => new EditableInput
                {
                    InputName = pair.Key,
                    InputType = draftResult.Template.Inputs.TryGetValue(pair.Key, out var type) ? type : "string",
                    NodeId = pair.Value.NodeId,
                    NodeClass = pair.Value.NodeClass,
                    NodeIndex = pair.Value.NodeIndex,
                    Field = pair.Value.Field
                })
                .ToList();

            return Page();
        }

        var key = Uri.UnescapeDataString(id);
        var split = key.Split(':', 2);
        if (split.Length != 2)
        {
            return NotFound("Template identifier is invalid.");
        }

        var template = await templateService.GetTemplateAsync(split[0], split[1], cancellationToken);
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

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Template name is required.";
            return Page();
        }

        JsonNode workflowNode;
        try
        {
            workflowNode = JsonNode.Parse(WorkflowJson) ?? throw new InputValidationException("Workflow JSON is required.");
            using var doc = JsonDocument.Parse(WorkflowJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InputValidationException("Workflow JSON must be an object.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InputValidationException)
        {
            ErrorMessage = ex.Message;
            return Page();
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

        try
        {
            await templateService.SaveTemplateAsync(new WorkflowTemplate
            {
                Name = Name,
                Version = Version,
                Category = Category,
                Workflow = workflowNode,
                Inputs = inputs,
                Mapping = mapping
            }, cancellationToken);
        }
        catch (InputValidationException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }

        if (IsDraft && !string.IsNullOrWhiteSpace(DraftId))
        {
            draftStore.RemoveDraft(DraftId);
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
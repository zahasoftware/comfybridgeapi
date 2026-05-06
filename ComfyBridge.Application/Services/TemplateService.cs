using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Services;

public sealed class TemplateService(
    ITemplateRepository templateRepository,
    IWorkflowInjectionService workflowInjectionService) : ITemplateService
{
    public Task<IReadOnlyCollection<WorkflowTemplate>> GetTemplatesAsync(CancellationToken cancellationToken) =>
        templateRepository.GetAllTemplatesAsync(cancellationToken);

    public async Task<WorkflowTemplate> GetTemplateAsync(string name, string? version, CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetTemplateAsync(name, version, cancellationToken);
        return template ?? throw new TemplateNotFoundException(name, version);
    }

    public async Task<WorkflowTemplate> SaveTemplateAsync(WorkflowTemplate template, CancellationToken cancellationToken)
    {
        ValidateTemplate(template, workflowInjectionService);

        await templateRepository.SaveTemplateAsync(template, cancellationToken);
        return template;
    }

    public async Task DeleteTemplateAsync(string name, string version, CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetTemplateAsync(name, version, cancellationToken);
        if (existing is null)
        {
            throw new TemplateNotFoundException(name, version);
        }

        await templateRepository.DeleteTemplateAsync(name, version, cancellationToken);
    }

    private static void ValidateTemplate(WorkflowTemplate template, IWorkflowInjectionService injectionService)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new InputValidationException("Template name is required.");
        }

        if (string.IsNullOrWhiteSpace(template.Version))
        {
            throw new InputValidationException("Template version is required.");
        }

        if (template.Workflow is not JsonObject workflowObject)
        {
            throw new InputValidationException("Workflow payload must be a JSON object keyed by node id.");
        }

        foreach (var inputName in template.Inputs.Keys)
        {
            if (!template.Mapping.ContainsKey(inputName))
            {
                throw new InputValidationException($"Input '{inputName}' is missing a mapping definition.");
            }
        }

        foreach (var inputName in template.Mapping.Keys.ToList())
        {
            if (!template.Inputs.ContainsKey(inputName))
            {
                throw new InputValidationException($"Mapping '{inputName}' does not exist in declared inputs.");
            }

            var normalized = NormalizeMapping(workflowObject, inputName, template.Mapping[inputName]);
            template.Mapping[inputName] = normalized;
            ValidateMapping(workflowObject, inputName, normalized);
        }

        EnsureExampleRequest(template);
        ValidateExampleRequest(template, injectionService);
    }

    private static void EnsureExampleRequest(WorkflowTemplate template)
    {
        if (template.ExampleRequest is not null)
        {
            return;
        }

        var inputs = new JsonObject();
        foreach (var (name, type) in template.Inputs)
        {
            inputs[name] = type.Trim().ToLowerInvariant() switch
            {
                "int" or "integer" => JsonValue.Create(1),
                "float" or "double" or "number" => JsonValue.Create(1.0),
                "bool" or "boolean" => JsonValue.Create(false),
                _ => JsonValue.Create("example-value")
            };
        }

        template.ExampleRequest = new JsonObject
        {
            ["template"] = $"{template.Name}:{template.Version}",
            ["inputs"] = inputs
        };
    }

    private static void ValidateExampleRequest(WorkflowTemplate template, IWorkflowInjectionService injectionService)
    {
        var example = template.ExampleRequest
            ?? throw new InputValidationException("Template example request is required.");

        if (!example.TryGetPropertyValue("template", out var templateNode) ||
            templateNode is not JsonValue templateValue ||
            !templateValue.TryGetValue<string>(out var templateRef) ||
            string.IsNullOrWhiteSpace(templateRef))
        {
            throw new InputValidationException("Example request requires a non-empty 'template' field.");
        }

        if (!example.TryGetPropertyValue("inputs", out var inputsNode) || inputsNode is not JsonObject inputs)
        {
            throw new InputValidationException("Example request requires an 'inputs' JSON object.");
        }

        foreach (var (inputName, inputType) in template.Inputs)
        {
            if (!inputs.TryGetPropertyValue(inputName, out var value) || value is null)
            {
                throw new InputValidationException($"Example request is missing input '{inputName}'.");
            }

            if (!IsTypeMatch(inputType, value))
            {
                throw new InputValidationException($"Example input '{inputName}' does not match declared type '{inputType}'.");
            }
        }

        injectionService.InjectInputs(template, (JsonObject)inputs.DeepClone());
    }

    private static bool IsTypeMatch(string expectedType, JsonNode value)
    {
        expectedType = expectedType.Trim().ToLowerInvariant();

        return expectedType switch
        {
            "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
            "image" or "file" => value is JsonValue fileValue && fileValue.TryGetValue<string>(out _),
            "int" or "integer" => value is JsonValue integerValue && integerValue.TryGetValue<int>(out _),
            "float" or "double" or "number" => value is JsonValue numberValue &&
                (numberValue.TryGetValue<double>(out _) || numberValue.TryGetValue<decimal>(out _)),
            "bool" or "boolean" => value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _),
            _ => true
        };
    }

    private static WorkflowInputMapping NormalizeMapping(JsonObject workflowObject, string inputName, WorkflowInputMapping mapping)
    {
        var hasNodeId = !string.IsNullOrWhiteSpace(mapping.NodeId);
        var hasNodeClass = !string.IsNullOrWhiteSpace(mapping.NodeClass);
        if (!hasNodeId || hasNodeClass || string.IsNullOrWhiteSpace(mapping.Field))
        {
            return mapping;
        }

        if (TryFindNodeById(workflowObject, mapping.NodeId!, out _))
        {
            return mapping;
        }

        if (!TryResolveNodeByField(workflowObject, mapping.Field!, out var resolvedNodeClass, out var resolvedNodeIndex))
        {
            return mapping;
        }

        return new WorkflowInputMapping
        {
            NodeClass = resolvedNodeClass,
            NodeIndex = resolvedNodeIndex,
            Field = mapping.Field,
            JsonPointer = mapping.JsonPointer
        };
    }

    private static void ValidateMapping(JsonObject workflowObject, string inputName, WorkflowInputMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.Field))
        {
            throw new InputValidationException($"Mapping field is required for input '{inputName}'.");
        }

        var hasNodeId = !string.IsNullOrWhiteSpace(mapping.NodeId);
        var hasNodeClass = !string.IsNullOrWhiteSpace(mapping.NodeClass);
        var hasJsonPointer = !string.IsNullOrWhiteSpace(mapping.JsonPointer);

        if (!hasNodeId && !hasNodeClass && !hasJsonPointer)
        {
            throw new InputValidationException($"Mapping for input '{inputName}' must include nodeId, nodeClass, or jsonPointer.");
        }

        if (hasNodeId)
        {
            if (TryFindNodeById(workflowObject, mapping.NodeId!, out _))
            {
                return;
            }

            if (!hasNodeClass)
            {
                throw new InputValidationException($"Mapped nodeId '{mapping.NodeId}' for input '{inputName}' does not exist.");
            }
        }

        if (!hasNodeClass)
        {
            return;
        }

        var matches = EnumerateWorkflowNodes(workflowObject)
            .Where(node => string.Equals(node["class_type"]?.GetValue<string>(), mapping.NodeClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new InputValidationException($"Mapped nodeClass '{mapping.NodeClass}' for input '{inputName}' does not exist.");
        }

        if (mapping.NodeIndex < 0 || mapping.NodeIndex >= matches.Count)
        {
            throw new InputValidationException($"Mapped nodeIndex '{mapping.NodeIndex}' for input '{inputName}' is out of range.");
        }
    }

    private static bool TryFindNodeById(JsonObject workflowObject, string nodeId, out JsonObject? node)
    {
        if (workflowObject[nodeId] is JsonObject directNode)
        {
            node = directNode;
            return true;
        }

        foreach (var candidate in EnumerateWorkflowNodes(workflowObject))
        {
            var idValue = candidate["id"]?.ToString();
            if (string.Equals(idValue, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                node = candidate;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static IEnumerable<JsonObject> EnumerateWorkflowNodes(JsonObject workflowObject)
    {
        foreach (var pair in workflowObject)
        {
            if (pair.Value is JsonObject node)
            {
                yield return node;
            }
        }

        if (workflowObject["nodes"] is not JsonArray nodes)
        {
            yield break;
        }

        foreach (var item in nodes)
        {
            if (item is JsonObject node)
            {
                yield return node;
            }
        }
    }

    private static bool TryResolveNodeByField(JsonObject workflowObject, string field, out string nodeClass, out int nodeIndex)
    {
        var candidates = EnumerateWorkflowNodes(workflowObject)
            .Select(node =>
            {
                var classType = node["class_type"]?.GetValue<string>();
                var hasField = node["inputs"] is JsonObject inputs && inputs.ContainsKey(field);
                return new { classType, hasField };
            })
            .Where(x => x.hasField && !string.IsNullOrWhiteSpace(x.classType))
            .ToList();

        if (candidates.Count == 0)
        {
            nodeClass = string.Empty;
            nodeIndex = 0;
            return false;
        }

        // Prefer deterministic mapping only when a single class type owns this input field.
        var classGroups = candidates
            .GroupBy(x => x.classType!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (classGroups.Count != 1)
        {
            nodeClass = string.Empty;
            nodeIndex = 0;
            return false;
        }

        nodeClass = classGroups[0].Key;
        nodeIndex = 0;
        return true;
    }
}
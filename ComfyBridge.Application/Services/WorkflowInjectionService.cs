using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Services;

public sealed class WorkflowInjectionService : IWorkflowInjectionService
{
    public JsonNode InjectInputs(WorkflowTemplate template, JsonObject inputs)
    {
        ValidateInputs(template, inputs);
        var workflow = template.Workflow.DeepClone();

        foreach (var (inputName, value) in inputs)
        {
            if (!template.Mapping.TryGetValue(inputName, out var mapping))
            {
                continue;
            }

            ApplyMapping(workflow, mapping, value);
        }

        return workflow;
    }

    private static void ValidateInputs(WorkflowTemplate template, JsonObject inputs)
    {
        foreach (var (name, type) in template.Inputs)
        {
            if (!inputs.TryGetPropertyValue(name, out var value) || value is null)
            {
                throw new InputValidationException($"Missing required input '{name}'.");
            }

            if (!IsTypeMatch(type, value))
            {
                throw new InputValidationException($"Input '{name}' expected type '{type}'.");
            }
        }
    }

    private static bool IsTypeMatch(string expectedType, JsonNode value)
    {
        expectedType = expectedType.Trim().ToLowerInvariant();

        return expectedType switch
        {
            "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
            "int" or "integer" => value is JsonValue integerValue && integerValue.TryGetValue<int>(out _),
            "float" or "double" or "number" => value is JsonValue numberValue && (numberValue.TryGetValue<double>(out _) || numberValue.TryGetValue<decimal>(out _)),
            "bool" or "boolean" => value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _),
            _ => true
        };
    }

    private static void ApplyMapping(JsonNode workflow, WorkflowInputMapping mapping, JsonNode? value)
    {
        if (!string.IsNullOrWhiteSpace(mapping.JsonPointer))
        {
            SetByJsonPointer(workflow, mapping.JsonPointer!, value);
            return;
        }

        if (!string.IsNullOrWhiteSpace(mapping.NodeClass) && !string.IsNullOrWhiteSpace(mapping.Field))
        {
            SetByNodeClass(workflow, mapping.NodeClass!, mapping.NodeIndex, mapping.Field!, value);
            return;
        }

        if (!string.IsNullOrWhiteSpace(mapping.NodeId) && !string.IsNullOrWhiteSpace(mapping.Field))
        {
            SetByNodeId(workflow, mapping.NodeId!, mapping.Field!, value);
            return;
        }

        throw new InputValidationException("Invalid template mapping. Either jsonPointer, nodeClass+field, or nodeId+field must be configured.");
    }

    private static void SetByNodeClass(JsonNode workflow, string nodeClass, int nodeIndex, string field, JsonNode? value)
    {
        if (workflow is not JsonObject root)
        {
            throw new InputValidationException("Workflow root must be a JSON object.");
        }

        var candidates = EnumerateWorkflowNodes(root)
            .Where(node => string.Equals(node["class_type"]?.GetValue<string>(), nodeClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= nodeIndex)
        {
            throw new InputValidationException($"Could not find node class '{nodeClass}' at index {nodeIndex}.");
        }

        SetNodeInputField(candidates[nodeIndex], field, value);
    }

    private static void SetByNodeId(JsonNode workflow, string nodeId, string field, JsonNode? value)
    {
        if (workflow is not JsonObject root)
        {
            throw new InputValidationException("Workflow root must be a JSON object.");
        }

        if (root[nodeId] is JsonObject directNode)
        {
            SetNodeInputField(directNode, field, value);
            return;
        }

        foreach (var node in EnumerateWorkflowNodes(root))
        {
            var idValue = node["id"]?.ToString();
            if (string.Equals(idValue, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                SetNodeInputField(node, field, value);
                return;
            }
        }

        throw new InputValidationException($"Mapped nodeId '{nodeId}' does not exist in workflow JSON.");
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

    private static void SetNodeInputField(JsonObject node, string field, JsonNode? value)
    {
        if (node["inputs"] is not JsonObject inputs)
        {
            throw new InputValidationException("Target node does not contain an inputs object.");
        }

        if (!inputs.ContainsKey(field))
        {
            throw new InputValidationException($"Target node does not contain input field '{field}'.");
        }

        inputs[field] = value?.DeepClone();
    }

    private static void SetByJsonPointer(JsonNode node, string pointer, JsonNode? value)
    {
        if (string.IsNullOrWhiteSpace(pointer) || pointer[0] != '/')
        {
            throw new InputValidationException($"Invalid jsonPointer '{pointer}'.");
        }

        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();

        JsonNode current = node;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            current = current switch
            {
                JsonObject obj when obj[segment] is not null => obj[segment]!,
                JsonArray arr when int.TryParse(segment, out var index) && index >= 0 && index < arr.Count => arr[index]!,
                _ => throw new InputValidationException($"jsonPointer '{pointer}' does not exist in workflow JSON.")
            };
        }

        var lastSegment = segments[^1];
        switch (current)
        {
            case JsonObject obj:
                obj[lastSegment] = value?.DeepClone();
                break;
            case JsonArray arr when int.TryParse(lastSegment, out var index) && index >= 0 && index < arr.Count:
                arr[index] = value?.DeepClone();
                break;
            default:
                throw new InputValidationException($"jsonPointer '{pointer}' targets an invalid location.");
        }
    }
}
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages.Workflows;

public sealed class TestModel(ITemplateService templateService, IGenerationService generationService) : PageModel
{
    [BindProperty]
    public string TemplateName { get; set; } = string.Empty;

    [BindProperty]
    public string TemplateVersion { get; set; } = string.Empty;

    [BindProperty]
    public List<TestInputEntry> Inputs { get; set; } = [];

    public string? ErrorMessage { get; private set; }

    public string? JobId { get; private set; }

    public string? JobStatus { get; private set; }

    public string WindowsCurlExample { get; private set; } = string.Empty;

    public string BashCurlExample { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var key = Uri.UnescapeDataString(id);
        var split = key.Split(':', 2);
        if (split.Length != 2)
        {
            return NotFound("Template identifier is invalid.");
        }

        var template = await templateService.GetTemplateAsync(split[0], split[1], cancellationToken);
        TemplateName = template.Name;
        TemplateVersion = template.Version;
        Inputs = template.Inputs
            .Select(pair => new TestInputEntry
            {
                Name = pair.Key,
                Type = pair.Value,
                Value = GetDefaultValue(pair.Value)
            })
            .ToList();

        BuildCurlExamples();
        return Page();
    }

    public async Task<IActionResult> OnPostExecuteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TemplateName) || string.IsNullOrWhiteSpace(TemplateVersion))
        {
            ErrorMessage = "Template name and version are required.";
            BuildCurlExamples();
            return Page();
        }

        try
        {
            var inputsObject = BuildInputsObject(Inputs);
            var job = await generationService.StartImageGenerationAsync($"{TemplateName}:{TemplateVersion}", inputsObject, cancellationToken);
            JobId = job.JobId;
            JobStatus = job.Status.ToString();
            BuildCurlExamples();
            return Page();
        }
        catch (InputValidationException ex)
        {
            ErrorMessage = ex.Message;
            BuildCurlExamples();
            return Page();
        }
    }

    private static JsonObject BuildInputsObject(IEnumerable<TestInputEntry> inputs)
    {
        var json = new JsonObject();
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                continue;
            }

            var type = (input.Type ?? "string").Trim().ToLowerInvariant();
            var raw = input.Value ?? string.Empty;

            json[input.Name] = type switch
            {
                "int" or "integer" => int.TryParse(raw, out var intValue)
                    ? JsonValue.Create(intValue)
                    : throw new InputValidationException($"Input '{input.Name}' must be a valid integer."),
                "float" or "double" or "number" => double.TryParse(raw, out var doubleValue)
                    ? JsonValue.Create(doubleValue)
                    : throw new InputValidationException($"Input '{input.Name}' must be a valid number."),
                "bool" or "boolean" => bool.TryParse(raw, out var boolValue)
                    ? JsonValue.Create(boolValue)
                    : throw new InputValidationException($"Input '{input.Name}' must be true or false."),
                _ => JsonValue.Create(raw)
            };
        }

        return json;
    }

    private void BuildCurlExamples()
    {
        var payload = BuildExamplePayload();
        WindowsCurlExample =
            "curl.exe -X POST \"http://localhost:5176/api/v1/generate/image\" -H \"Content-Type: application/json\" -d \"" +
            payload.Replace("\"", "\\\"") +
            "\"";

        BashCurlExample =
            "curl -X POST 'http://localhost:5176/api/v1/generate/image' \\\n  -H 'Content-Type: application/json' \\\n  -d '" + payload.Replace("'", "'\\''") + "'";
    }

    private string BuildExamplePayload()
    {
        var exampleInputs = new JsonObject();
        foreach (var input in Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                continue;
            }

            var type = (input.Type ?? "string").Trim().ToLowerInvariant();
            exampleInputs[input.Name] = type switch
            {
                "int" or "integer" => JsonValue.Create(30),
                "float" or "double" or "number" => JsonValue.Create(7.5),
                "bool" or "boolean" => JsonValue.Create(false),
                _ => JsonValue.Create(input.Value ?? "example-value")
            };
        }

        var payload = new JsonObject
        {
            ["template"] = $"{TemplateName}:{TemplateVersion}",
            ["inputs"] = exampleInputs
        };

        return payload.ToJsonString();
    }

    private static string GetDefaultValue(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => "30",
            "float" or "double" or "number" => "7.5",
            "bool" or "boolean" => "false",
            _ => "example-value"
        };
    }

    public sealed class TestInputEntry
    {
        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = "string";

        public string? Value { get; set; }
    }
}

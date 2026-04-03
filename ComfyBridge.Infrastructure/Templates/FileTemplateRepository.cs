using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Models;
using ComfyBridge.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Infrastructure.Templates;

public sealed class FileTemplateRepository(IOptions<TemplateStorageOptions> options) : ITemplateRepository
{
    private const string TemplateFileName = "template.json";
    private const string RawWorkflowFileName = "raw-workflow.json";
    private const string MappingFileName = "mapping.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<IReadOnlyCollection<WorkflowTemplate>> GetAllTemplatesAsync(CancellationToken cancellationToken)
    {
        var path = ResolveTemplatesPath();
        if (!Directory.Exists(path))
        {
            return [];
        }

        var templates = new List<WorkflowTemplate>();

        // Legacy flat-file templates: {name}.{version}.json
        var legacyFiles = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                var fileName = Path.GetFileName(file);
                return !string.Equals(fileName, TemplateFileName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(fileName, RawWorkflowFileName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(fileName, MappingFileName, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        foreach (var file in legacyFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var template = await ReadTemplateFileAsync(file, cancellationToken);
            if (template is not null)
            {
                templates.Add(template);
            }
        }

        // New subfolder layout: {name}.{version}/template.json + raw-workflow.json + mapping.json
        foreach (var folder in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var templatePath = Path.Combine(folder, TemplateFileName);
            var template = await ReadTemplateFileAsync(templatePath, cancellationToken);
            if (template is not null)
            {
                var rawWorkflowPath = Path.Combine(folder, RawWorkflowFileName);
                if (File.Exists(rawWorkflowPath))
                {
                    var workflowRaw = await File.ReadAllTextAsync(rawWorkflowPath, cancellationToken);
                    var workflowNode = JsonNode.Parse(workflowRaw);
                    if (workflowNode is not null)
                    {
                        template = new WorkflowTemplate
                        {
                            Name = template.Name,
                            Version = template.Version,
                            Inputs = template.Inputs,
                            Mapping = template.Mapping,
                            ExampleRequest = template.ExampleRequest,
                            Workflow = workflowNode
                        };
                    }
                }

                // If mapping.json exists it is the authoritative source for inputs/mapping
                var mappingFilePath = Path.Combine(folder, MappingFileName);
                if (File.Exists(mappingFilePath))
                {
                    await using var ms = File.OpenRead(mappingFilePath);
                    var dto = await JsonSerializer.DeserializeAsync<MappingFileDto>(ms, JsonOptions, cancellationToken);
                    if (dto is not null)
                    {
                        template = new WorkflowTemplate
                        {
                            Name = template.Name,
                            Version = template.Version,
                            Inputs = dto.Inputs,
                            Mapping = dto.Mapping,
                            ExampleRequest = template.ExampleRequest,
                            Workflow = template.Workflow
                        };
                    }
                }

                templates.Add(template);
            }
        }

        return templates;
    }

    public async Task<WorkflowTemplate?> GetTemplateAsync(string name, string? version, CancellationToken cancellationToken)
    {
        var templates = await GetAllTemplatesAsync(cancellationToken);

        var candidates = templates
            .Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(version))
        {
            return candidates.FirstOrDefault(t => string.Equals(t.Version, version, StringComparison.OrdinalIgnoreCase));
        }

        return candidates
            .OrderByDescending(t => ParseVersion(t.Version))
            .ThenByDescending(t => t.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public async Task SaveTemplateAsync(WorkflowTemplate template, CancellationToken cancellationToken)
    {
        var path = ResolveTemplatesPath();
        Directory.CreateDirectory(path);

        var folderName = BuildFolderName(template.Name, template.Version);
        var fullFolderPath = Path.Combine(path, folderName);
        Directory.CreateDirectory(fullFolderPath);

        var templatePath = Path.Combine(fullFolderPath, TemplateFileName);
        var workflowPath = Path.Combine(fullFolderPath, RawWorkflowFileName);
        var mappingPath = Path.Combine(fullFolderPath, MappingFileName);

        await using (var stream = File.Create(templatePath))
        {
            await JsonSerializer.SerializeAsync(stream, template, JsonOptions, cancellationToken);
        }

        var workflowJson = template.Workflow.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(workflowPath, workflowJson, cancellationToken);

        var mappingDto = new MappingFileDto { Inputs = template.Inputs, Mapping = template.Mapping };
        await using (var stream = File.Create(mappingPath))
        {
            await JsonSerializer.SerializeAsync(stream, mappingDto, PrettyJsonOptions, cancellationToken);
        }
    }

    public Task DeleteTemplateAsync(string name, string version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveTemplatesPath();
        var fileName = BuildLegacyFileName(name, version);
        var fullPath = Path.Combine(path, fileName);
        var folderPath = Path.Combine(path, BuildFolderName(name, version));

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task<WorkflowTemplate?> ReadTemplateFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<WorkflowTemplate>(json, JsonOptions);
        }
        catch (JsonException)
        {
            var workflow = JsonNode.Parse(json);
            if (workflow is null || !LooksLikeWorkflowPayload(workflow))
            {
                return null;
            }

            var (name, version) = ParseLegacyTemplateFileName(Path.GetFileName(path));
            return new WorkflowTemplate
            {
                Name = name,
                Version = version,
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Mapping = new Dictionary<string, WorkflowInputMapping>(StringComparer.OrdinalIgnoreCase),
                Workflow = workflow
            };
        }
    }

    private static bool LooksLikeWorkflowPayload(JsonNode workflow)
    {
        if (workflow is not JsonObject workflowObject || workflowObject.Count == 0)
        {
            return false;
        }

        foreach (var pair in workflowObject)
        {
            if (pair.Value is not JsonObject nodeObject)
            {
                continue;
            }

            if (nodeObject["class_type"] is JsonValue && nodeObject["inputs"] is JsonObject)
            {
                return true;
            }
        }

        return false;
    }

    private static (string Name, string Version) ParseLegacyTemplateFileName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExtension.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 3 && int.TryParse(parts[^2], out _) && int.TryParse(parts[^1], out _))
        {
            return (string.Join('.', parts[..^2]), $"{parts[^2]}.{parts[^1]}");
        }

        if (parts.Length >= 2)
        {
            return (string.Join('.', parts[..^1]), parts[^1]);
        }

        return (nameWithoutExtension, "1.0");
    }

    private string ResolveTemplatesPath()
    {
        var configuredPath = options.Value.TemplatesPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) ? version : new Version(0, 0);

    private static string BuildLegacyFileName(string name, string version)
    {
        var safeName = Sanitize(name);
        var safeVersion = Sanitize(version);
        return $"{safeName}.{safeVersion}.json";
    }

    private static string BuildFolderName(string name, string version)
    {
        var safeName = Sanitize(name);
        var safeVersion = Sanitize(version);
        return $"{safeName}.{safeVersion}";
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = value
            .Where(ch => !invalid.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch))
            .ToArray();

        return new string(buffer).Trim('-');
    }

    private sealed class MappingFileDto
    {
        public Dictionary<string, string> Inputs { get; init; } = [];
        public Dictionary<string, WorkflowInputMapping> Mapping { get; init; } = [];
    }
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Application.Models;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using ComfyBridge.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Infrastructure.Ai;

public sealed class AiWorkflowClient(HttpClient httpClient, IOptions<WorkflowAiOptions> options) : IAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TemplateDefinition> AnalyzeWorkflowAsync(string workflowJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            throw new InputValidationException("Workflow JSON is required.");
        }

        var heuristicTemplate = TryAnalyzeHeuristically(workflowJson);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return heuristicTemplate ?? throw new InputValidationException("Could not analyze workflow JSON.");
        }

        try
        {
            var maxAttempts = Math.Clamp(settings.MaxTemplateValidationAttempts, 1, 10);
            var lastFailure = string.Empty;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var responsePayload = await CallAiAsync(workflowJson, settings, cancellationToken, attempt, maxAttempts, lastFailure);
                    var template = ParseTemplate(responsePayload);
                    template = MergeWithHeuristicCoverage(template, heuristicTemplate);
                    ValidateTemplateExample(template, workflowJson);
                    return template;
                }
                catch (Exception ex) when (ex is JsonException or InputValidationException)
                {
                    lastFailure = ex.Message;
                    if (attempt == maxAttempts)
                    {
                        throw;
                    }
                }
            }

            throw new InputValidationException("AI template analysis failed after all attempts.");
        }
        catch when (settings.AllowHeuristicFallback)
        {
            return heuristicTemplate ?? AnalyzeHeuristically(workflowJson);
        }
    }

    private async Task<string> CallAiAsync(
        string workflowJson,
        WorkflowAiOptions settings,
        CancellationToken cancellationToken,
        int attempt,
        int maxAttempts,
        string previousFailure)
    {
        var provider = settings.Provider.Trim();
        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return await CallOllamaAsync(workflowJson, settings, cancellationToken, attempt, maxAttempts, previousFailure);
        }

        return await CallCustomAsync(workflowJson, settings, cancellationToken, attempt, maxAttempts, previousFailure);
    }

    private async Task<string> CallOllamaAsync(
        string workflowJson,
        WorkflowAiOptions settings,
        CancellationToken cancellationToken,
        int attempt,
        int maxAttempts,
        string previousFailure)
    {
        var endpoint = settings.Endpoint.TrimEnd('/');
        var requestBody = new
        {
            model = settings.Model,
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0,
                top_p = 1
            },
            prompt = BuildPrompt(workflowJson, attempt, maxAttempts, previousFailure)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/generate")
        {
            Content = JsonContent.Create(requestBody)
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("response", out var inner) && inner.ValueKind == JsonValueKind.String)
        {
            return inner.GetString() ?? throw new InputValidationException("AI response body was empty.");
        }

        return raw;
    }

    private async Task<string> CallCustomAsync(
        string workflowJson,
        WorkflowAiOptions settings,
        CancellationToken cancellationToken,
        int attempt,
        int maxAttempts,
        string previousFailure)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                workflowJson,
                instructions = BuildPrompt(workflowJson, attempt, maxAttempts, previousFailure)
            })
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string BuildPrompt(string workflowJson, int attempt, int maxAttempts, string previousFailure)
    {
        var sb = new StringBuilder();
            sb.AppendLine("You are a ComfyUI workflow analyzer. Analyze the workflow JSON and return ONLY valid JSON — no markdown, no code fences, no explanation.");
            if (attempt > 1)
                sb.AppendLine($"Attempt {attempt} of {maxAttempts}. Fix ALL issues listed at the end before anything else.");
            sb.AppendLine();
            sb.AppendLine("## REQUIRED OUTPUT SCHEMA");
            sb.AppendLine("{");
            sb.AppendLine("  \"name\": \"<kebab-case-descriptive-name>\",");
            sb.AppendLine("  \"inputs\": { \"<camelCaseInputName>\": \"<type>\" },");
            sb.AppendLine("  \"mapping\": {");
            sb.AppendLine("    \"<camelCaseInputName>\": {");
            sb.AppendLine("      \"nodeId\": \"<node-key-in-workflow>\",");
            sb.AppendLine("      \"nodeClass\": \"<class_type-value>\",");
            sb.AppendLine("      \"nodeIndex\": 0,");
            sb.AppendLine("      \"field\": \"<exact-key-inside-node-inputs>\"");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"exampleRequest\": {");
            sb.AppendLine("    \"template\": \"<name>:1.0\",");
            sb.AppendLine("    \"inputs\": { \"<camelCaseInputName>\": <value> }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("## FIELD DETECTION RULES");
            sb.AppendLine("Map EVERY user-editable field the user can change.");
            sb.AppendLine("A field is mappable when its effective value resolves to a literal string, number, or bool.");
            sb.AppendLine("That literal may appear directly in the target node, OR indirectly through a referenced Primitive node such as PrimitiveInt/PrimitiveFloat/PrimitiveBoolean.");
            sb.AppendLine("If a node input is an array like [\"128:125\", 0], inspect the referenced node. If that referenced node exposes a literal 'value', map the user-facing field to that Primitive node's 'value' field.");
            sb.AppendLine();
            sb.AppendLine("### CLIPTextEncode  (field: 'text')");
            sb.AppendLine("  First occurrence  → inputName: \"prompt\",         type: \"string\",  example: \"a cinematic landscape, photorealistic\"");
            sb.AppendLine("  Second occurrence → inputName: \"negativePrompt\", type: \"string\",  example: \"blurry, low quality, watermark\"");
            sb.AppendLine("  (If only one CLIPTextEncode node exists, map only \"prompt\".)");
            sb.AppendLine();
            sb.AppendLine("### KSampler / KSamplerAdvanced");
            sb.AppendLine("  'steps'        → inputName: \"steps\",     type: \"int\",    example: 20");
            sb.AppendLine("  'cfg'          → inputName: \"cfg\",       type: \"float\",  example: 7.0");
            sb.AppendLine("  'seed'         → inputName: \"seed\",      type: \"int\",    example: 42");
            sb.AppendLine("  'noise_seed'   → inputName: \"seed\",      type: \"int\",    example: 42");
            sb.AppendLine("  'sampler_name' → inputName: \"sampler\",   type: \"string\", example: \"euler\"");
            sb.AppendLine("  'scheduler'    → inputName: \"scheduler\", type: \"string\", example: \"normal\"");
            sb.AppendLine("  'denoise'      → inputName: \"denoise\",   type: \"float\",  example: 1.0  (only if its value is a literal, not an array)");
            sb.AppendLine();
            sb.AppendLine("### EmptyLatentImage / EmptySD3LatentImage / EmptyHunyuanLatentVideo / EmptyHunyuanLatentImage / EmptyVideoLatent");
            sb.AppendLine("  'width'      → inputName: \"width\",     type: \"int\", example: 512");
            sb.AppendLine("  'height'     → inputName: \"height\",    type: \"int\", example: 512");
            sb.AppendLine("  'batch_size' → inputName: \"batchSize\", type: \"int\", example: 1  (only if present)");
            sb.AppendLine("  'length'     → inputName: \"frameCount\", type: \"int\", example: 81  (only if the effective value resolves to a literal; otherwise skip)");
            sb.AppendLine();
            sb.AppendLine("### CreateVideo / SaveVideo / Video workflows");
            sb.AppendLine("  'fps'            → inputName: \"fps\",            type: \"float\",  example: 16");
            sb.AppendLine("  'filename_prefix'→ inputName: \"filenamePrefix\", type: \"string\", example: \"video/ComfyUI\"");
            sb.AppendLine("  If fps is connected through a Primitive node, map to that Primitive node's literal 'value'.");
            sb.AppendLine();
            sb.AppendLine("### PrimitiveInt / PrimitiveFloat / PrimitiveBoolean");
            sb.AppendLine("  Primitive nodes are often the real editable control in text-to-video workflows.");
            sb.AppendLine("  If a computation or switch node references Primitive values for steps, cfg, fps, duration, frame count, seed, width, or height, map the user-facing input to the Primitive node's 'value' field.");
            sb.AppendLine("  Use semantic names such as \"fps\", \"duration\", \"steps\", \"cfg\", \"seed\", \"width\", \"height\" when the title or downstream usage makes the meaning clear.");
            sb.AppendLine();
            sb.AppendLine("### CheckpointLoaderSimple / CheckpointLoader");
            sb.AppendLine("  'ckpt_name' → inputName: \"model\", type: \"string\", example: \"v1-5-pruned-emaonly.ckpt\"");
            sb.AppendLine();
            sb.AppendLine("### LoraLoader / LoraLoaderModelOnly");
            sb.AppendLine("  'lora_name'      → inputName: \"loraName\",     type: \"string\", example: \"my-lora.safetensors\"");
            sb.AppendLine("  'strength_model' → inputName: \"loraStrength\", type: \"float\",  example: 0.8");
            sb.AppendLine();
            sb.AppendLine("### LoadImage / LoadImageMask");
            sb.AppendLine("  'image' → inputName: \"image\", type: \"image\", example: \"input.png\"");
            sb.AppendLine();
            sb.AppendLine("### VAELoader");
            sb.AppendLine("  'vae_name' → inputName: \"vae\", type: \"string\", example: \"vae-ft-mse-840000-ema-pruned.ckpt\"");
            sb.AppendLine();
            sb.AppendLine("### UNETLoader / DiffusionModelLoader");
            sb.AppendLine("  'unet_name' → inputName: \"unetModel\", type: \"string\", example: \"flux1-dev.safetensors\"");
            sb.AppendLine();
            sb.AppendLine("### CLIPLoader");
            sb.AppendLine("  'clip_name' → inputName: \"clipModel\", type: \"string\", example: \"clip.safetensors\"");
            sb.AppendLine();
            sb.AppendLine("## STRICT RULES");
            sb.AppendLine("- SKIP raw array references themselves, but DO inspect them to find referenced Primitive literal values.");
            sb.AppendLine("- ONLY include fields actually present in this specific workflow.");
            sb.AppendLine("- When prompt, negativePrompt, steps, cfg, seed, width, height, sampler, scheduler, batch_size, fps, or model exist, you MUST include them.");
            sb.AppendLine("- In text-to-video workflows, prefer the actual editable controls, not only terminal save nodes.");
            sb.AppendLine("- Do NOT return only SaveImage.filename_prefix when richer generation controls exist in the workflow.");
            sb.AppendLine("- Do NOT return only SaveVideo.filename_prefix when richer video generation controls exist in the workflow.");
            sb.AppendLine("- SaveImage.filename_prefix is secondary metadata, not the primary mapping result for txt2img workflows.");
            sb.AppendLine("- exampleRequest.template must be exactly \"<name>:1.0\".");
            sb.AppendLine("- exampleRequest.inputs must include EVERY declared input with a value matching its type.");
            sb.AppendLine("- Allowed types: \"string\", \"int\", \"float\", \"bool\".");
            sb.AppendLine("- Return ONLY the JSON object. No markdown. No ```json. No extra text.");

            if (!string.IsNullOrWhiteSpace(previousFailure))
            {
                sb.AppendLine();
                sb.AppendLine("## PREVIOUS ATTEMPT FAILED — you MUST fix these issues:");
                sb.AppendLine(previousFailure);
            }

            sb.AppendLine();
            sb.AppendLine("## WORKFLOW JSON:");
            sb.AppendLine(workflowJson);
        return sb.ToString();
    }

    private static TemplateDefinition ParseTemplate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (LooksLikeWorkflowPayload(root))
        {
            return AnalyzeHeuristically(json);
        }

        var name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InputValidationException("AI template output must include a non-empty name.");
        }

        if (!root.TryGetProperty("inputs", out var inputsEl) || inputsEl.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("AI template output must include an inputs object.");
        }

        if (!root.TryGetProperty("mapping", out var mappingEl) || mappingEl.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("AI template output must include a mapping object.");
        }

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in inputsEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                throw new InputValidationException($"Input type for '{prop.Name}' must be a string.");
            }

            var value = prop.Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InputValidationException($"Input type for '{prop.Name}' cannot be empty.");
            }

            inputs[prop.Name] = value;
        }

        var mapping = new Dictionary<string, WorkflowInputMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in mappingEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InputValidationException($"Mapping for '{prop.Name}' must be an object.");
            }

            var map = prop.Value;
            var field = map.TryGetProperty("field", out var fieldEl) && fieldEl.ValueKind == JsonValueKind.String
                ? fieldEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(field))
            {
                throw new InputValidationException($"Mapping field for '{prop.Name}' is required.");
            }

            var nodeId = map.TryGetProperty("nodeId", out var nodeEl) && nodeEl.ValueKind == JsonValueKind.String
                ? nodeEl.GetString()
                : null;

            var nodeClass = map.TryGetProperty("nodeClass", out var classEl) && classEl.ValueKind == JsonValueKind.String
                ? classEl.GetString()
                : null;

            var nodeIndex = map.TryGetProperty("nodeIndex", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                ? idxEl.GetInt32()
                : 0;

            if (string.IsNullOrWhiteSpace(nodeId) && string.IsNullOrWhiteSpace(nodeClass))
            {
                throw new InputValidationException($"Mapping for '{prop.Name}' requires nodeId or nodeClass.");
            }

            mapping[prop.Name] = new WorkflowInputMapping
            {
                NodeId = nodeId,
                NodeClass = nodeClass,
                NodeIndex = nodeIndex,
                Field = field
            };
        }

        if (!root.TryGetProperty("exampleRequest", out var exampleRequestEl) || exampleRequestEl.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("AI template output must include an exampleRequest object.");
        }

        return new TemplateDefinition
        {
            Name = NormalizeName(name),
            Version = "1.0",
            Inputs = inputs,
            Mapping = mapping,
            ExampleRequest = JsonNode.Parse(exampleRequestEl.GetRawText())?.AsObject()
                ?? throw new InputValidationException("AI exampleRequest payload is invalid JSON.")
        };
    }

    private static TemplateDefinition AnalyzeHeuristically(string workflowJson)
    {
        using var doc = JsonDocument.Parse(workflowJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("Workflow JSON must be an object.");
        }

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mapping = new Dictionary<string, WorkflowInputMapping>(StringComparer.OrdinalIgnoreCase);
        var clipTextNodes = new List<(string NodeId, JsonElement Inputs)>();

        foreach (var node in root.EnumerateObject())
        {
            if (node.Value.ValueKind != JsonValueKind.Object ||
                !node.Value.TryGetProperty("class_type", out var classTypeEl) ||
                classTypeEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var classType = classTypeEl.GetString() ?? string.Empty;
            if (!node.Value.TryGetProperty("inputs", out var nodeInputs) || nodeInputs.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (string.Equals(classType, "CLIPTextEncode", StringComparison.OrdinalIgnoreCase) &&
                HasLiteralField(nodeInputs, "text"))
            {
                clipTextNodes.Add((node.Name, nodeInputs));
            }

            if (string.Equals(classType, "CheckpointLoaderSimple", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "CheckpointLoader", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("model", "string", node.Name, nodeInputs, "ckpt_name", inputs, mapping);
            }

            if (string.Equals(classType, "KSampler", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "KSamplerAdvanced", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("steps", "int", node.Name, nodeInputs, "steps", inputs, mapping);
                AddLiteralField("cfg", "float", node.Name, nodeInputs, "cfg", inputs, mapping);
                AddLiteralField("seed", "int", node.Name, nodeInputs, "seed", inputs, mapping);
                AddLiteralField("seed", "int", node.Name, nodeInputs, "noise_seed", inputs, mapping);
                AddLiteralField("sampler", "string", node.Name, nodeInputs, "sampler_name", inputs, mapping);
                AddLiteralField("scheduler", "string", node.Name, nodeInputs, "scheduler", inputs, mapping);
                AddLiteralField("denoise", "float", node.Name, nodeInputs, "denoise", inputs, mapping);
            }

            if (string.Equals(classType, "EmptyLatentImage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "EmptySD3LatentImage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "EmptyHunyuanLatentVideo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "EmptyHunyuanLatentImage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "EmptyVideoLatent", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("width", "int", node.Name, nodeInputs, "width", inputs, mapping);
                AddLiteralField("height", "int", node.Name, nodeInputs, "height", inputs, mapping);
                AddLiteralField("batchSize", "int", node.Name, nodeInputs, "batch_size", inputs, mapping);
            }

            if ((string.Equals(classType, "LoadImage", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(classType, "LoadImageMask", StringComparison.OrdinalIgnoreCase)) &&
                HasLiteralField(nodeInputs, "image"))
            {
                AddLiteralField("image", "image", node.Name, nodeInputs, "image", inputs, mapping);
            }

            if (string.Equals(classType, "VAELoader", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("vae", "string", node.Name, nodeInputs, "vae_name", inputs, mapping);
            }

            if (string.Equals(classType, "UNETLoader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "DiffusionModelLoader", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("unetModel", "string", node.Name, nodeInputs, "unet_name", inputs, mapping);
            }

            if (string.Equals(classType, "CLIPLoader", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("clipModel", "string", node.Name, nodeInputs, "clip_name", inputs, mapping);
            }

            if (string.Equals(classType, "LoraLoader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "LoraLoaderModelOnly", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("loraName", "string", node.Name, nodeInputs, "lora_name", inputs, mapping);
                AddLiteralField("loraStrength", "float", node.Name, nodeInputs, "strength_model", inputs, mapping);
            }

            if (string.Equals(classType, "SaveImage", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("filenamePrefix", "string", node.Name, nodeInputs, "filename_prefix", inputs, mapping);
            }

            if (string.Equals(classType, "SaveVideo", StringComparison.OrdinalIgnoreCase))
            {
                AddLiteralField("filenamePrefix", "string", node.Name, nodeInputs, "filename_prefix", inputs, mapping);
            }

            if (string.Equals(classType, "PrimitiveInt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "PrimitiveFloat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(classType, "PrimitiveBoolean", StringComparison.OrdinalIgnoreCase))
            {
                var title = GetNodeTitle(node.Value);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    AddPrimitiveFieldFromTitle(title, node.Name, nodeInputs, inputs, mapping);
                }
            }
        }

        if (clipTextNodes.Count > 0)
        {
            AddLiteralField("prompt", "string", clipTextNodes[0].NodeId, clipTextNodes[0].Inputs, "text", inputs, mapping);
        }

        if (clipTextNodes.Count > 1)
        {
            AddLiteralField("negativePrompt", "string", clipTextNodes[1].NodeId, clipTextNodes[1].Inputs, "text", inputs, mapping);
        }

        if (inputs.Count == 0)
        {
            throw new InputValidationException("Could not detect workflow inputs. Provide manual mapping in the review page.");
        }

        var template = new TemplateDefinition
        {
            Name = "auto-generated-workflow",
            Version = "1.0",
            Inputs = inputs,
            Mapping = mapping,
            ExampleRequest = BuildDefaultExampleRequest("auto-generated-workflow", "1.0", inputs)
        };

        ValidateTemplateExample(template, workflowJson);
        return template;
    }

    private static void ValidateTemplateExample(TemplateDefinition template, string workflowJson)
    {
        if (template.ExampleRequest is null)
        {
            throw new InputValidationException("AI template output must include exampleRequest.");
        }

        if (!template.ExampleRequest.TryGetPropertyValue("template", out var templateNameNode) ||
            templateNameNode is not JsonValue templateNameValue ||
            !templateNameValue.TryGetValue<string>(out var templateName) ||
            string.IsNullOrWhiteSpace(templateName))
        {
            throw new InputValidationException("exampleRequest.template must be a non-empty string.");
        }

        if (!template.ExampleRequest.TryGetPropertyValue("inputs", out var inputsNode) || inputsNode is not JsonObject exampleInputs)
        {
            throw new InputValidationException("exampleRequest.inputs must be a JSON object.");
        }

        foreach (var (inputName, inputType) in template.Inputs)
        {
            if (!exampleInputs.TryGetPropertyValue(inputName, out var value) || value is null)
            {
                throw new InputValidationException($"exampleRequest.inputs is missing required input '{inputName}'.");
            }

            if (!IsExpectedType(inputType, value))
            {
                throw new InputValidationException($"exampleRequest.inputs['{inputName}'] does not match declared type '{inputType}'.");
            }
        }

        ValidateMappingsAgainstWorkflow(template, workflowJson);
    }

    private static void ValidateMappingsAgainstWorkflow(TemplateDefinition template, string workflowJson)
    {
        using var workflowDoc = JsonDocument.Parse(workflowJson);
        if (workflowDoc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InputValidationException("Workflow JSON must be an object.");
        }

        foreach (var (inputName, mapping) in template.Mapping)
        {
            if (!template.Inputs.ContainsKey(inputName))
            {
                throw new InputValidationException($"Mapping '{inputName}' does not exist in inputs.");
            }

            if (string.IsNullOrWhiteSpace(mapping.Field))
            {
                throw new InputValidationException($"Mapping field for '{inputName}' is required.");
            }

            if (!TryResolveNode(workflowDoc.RootElement, mapping, out var node))
            {
                throw new InputValidationException($"Could not resolve target node for mapping '{inputName}'.");
            }

            if (!node.TryGetProperty("inputs", out var nodeInputs) || nodeInputs.ValueKind != JsonValueKind.Object)
            {
                throw new InputValidationException($"Target node for '{inputName}' does not contain an inputs object.");
            }

            if (!nodeInputs.TryGetProperty(mapping.Field, out _))
            {
                throw new InputValidationException($"Target node for '{inputName}' does not contain field '{mapping.Field}'.");
            }
        }
    }

    private static bool TryResolveNode(JsonElement root, WorkflowInputMapping mapping, out JsonElement node)
    {
        if (!string.IsNullOrWhiteSpace(mapping.NodeId))
        {
            if (root.TryGetProperty(mapping.NodeId, out var directNode) && directNode.ValueKind == JsonValueKind.Object)
            {
                node = directNode;
                return true;
            }

            foreach (var candidate in EnumerateWorkflowNodes(root))
            {
                if (candidate.TryGetProperty("id", out var idEl) && idEl.ToString() == mapping.NodeId)
                {
                    node = candidate;
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(mapping.NodeClass))
        {
            var matches = EnumerateWorkflowNodes(root)
                .Where(candidate =>
                    candidate.TryGetProperty("class_type", out var classEl) &&
                    classEl.ValueKind == JsonValueKind.String &&
                    string.Equals(classEl.GetString(), mapping.NodeClass, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var index = mapping.NodeIndex;
            if (index < 0 || index >= matches.Count)
            {
                node = default;
                return false;
            }

            node = matches[index];
            return true;
        }

        node = default;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateWorkflowNodes(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                yield return property.Value;
            }
        }

        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                yield return node;
            }
        }
    }

    private static JsonObject BuildDefaultExampleRequest(string templateName, string version, IReadOnlyDictionary<string, string> inputs)
    {
        var exampleInputs = new JsonObject();
        foreach (var (name, type) in inputs)
        {
            exampleInputs[name] = name switch
            {
                "prompt" => JsonValue.Create("a cinematic landscape, ultra detailed"),
                "negativePrompt" => JsonValue.Create("blurry, low quality, distorted"),
                "model" => JsonValue.Create("model.safetensors"),
                "sampler" => JsonValue.Create("euler"),
                "scheduler" => JsonValue.Create("normal"),
                "filenamePrefix" => JsonValue.Create("generated-image"),
                _ => type.Trim().ToLowerInvariant() switch
                {
                    "int" or "integer" => JsonValue.Create(1),
                    "float" or "double" or "number" => JsonValue.Create(1.0),
                    "bool" or "boolean" => JsonValue.Create(false),
                    _ => JsonValue.Create("example-value")
                }
            };
        }

        return new JsonObject
        {
            ["template"] = $"{templateName}:{version}",
            ["inputs"] = exampleInputs
        };
    }

    private static TemplateDefinition? TryAnalyzeHeuristically(string workflowJson)
    {
        try
        {
            return AnalyzeHeuristically(workflowJson);
        }
        catch
        {
            return null;
        }
    }

    private static TemplateDefinition MergeWithHeuristicCoverage(TemplateDefinition template, TemplateDefinition? heuristicTemplate)
    {
        if (heuristicTemplate is null)
        {
            return template;
        }

        var mergedInputs = new Dictionary<string, string>(heuristicTemplate.Inputs, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in template.Inputs)
        {
            mergedInputs[key] = value;
        }

        var mergedMapping = new Dictionary<string, WorkflowInputMapping>(heuristicTemplate.Mapping, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in template.Mapping)
        {
            mergedMapping[key] = value;
        }

        var mergedExampleRequest = BuildDefaultExampleRequest(
            NormalizeName(template.Name),
            string.IsNullOrWhiteSpace(template.Version) ? "1.0" : template.Version,
            mergedInputs);

        if (template.ExampleRequest.TryGetPropertyValue("inputs", out var aiInputsNode) && aiInputsNode is JsonObject aiInputs)
        {
            var mergedExampleInputs = mergedExampleRequest["inputs"]!.AsObject();
            foreach (var pair in aiInputs)
            {
                mergedExampleInputs[pair.Key] = pair.Value?.DeepClone();
            }
        }

        return new TemplateDefinition
        {
            Name = NormalizeName(template.Name),
            Version = string.IsNullOrWhiteSpace(template.Version) ? "1.0" : template.Version,
            Inputs = mergedInputs,
            Mapping = mergedMapping,
            ExampleRequest = mergedExampleRequest
        };
    }

    private static bool IsExpectedType(string expectedType, JsonNode value)
    {
        expectedType = expectedType.Trim().ToLowerInvariant();
        return expectedType switch
        {
            "string" => value is JsonValue stringValue && stringValue.TryGetValue<string>(out _),
            "image" or "file" => value is JsonValue fileValue && fileValue.TryGetValue<string>(out _),
            "int" or "integer" => value is JsonValue intValue && intValue.TryGetValue<int>(out _),
            "float" or "double" or "number" => value is JsonValue numberValue &&
                (numberValue.TryGetValue<double>(out _) || numberValue.TryGetValue<decimal>(out _)),
            "bool" or "boolean" => value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _),
            _ => true
        };
    }

    private static bool LooksLikeWorkflowPayload(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = root.EnumerateObject().ToList();
        if (properties.Count == 0)
        {
            return false;
        }

        var workflowNodeCount = 0;
        foreach (var property in properties)
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (property.Value.TryGetProperty("class_type", out var classType) &&
                classType.ValueKind == JsonValueKind.String &&
                property.Value.TryGetProperty("inputs", out var inputs) &&
                inputs.ValueKind == JsonValueKind.Object)
            {
                workflowNodeCount++;
            }
        }

        return workflowNodeCount > 0 && !root.TryGetProperty("mapping", out _);
    }

    private static bool HasLiteralField(JsonElement nodeInputs, string fieldName)
    {
        if (!nodeInputs.TryGetProperty(fieldName, out var value))
        {
            return false;
        }

        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
    }

    private static string? GetNodeTitle(JsonElement node)
    {
        if (node.TryGetProperty("_meta", out var meta) &&
            meta.ValueKind == JsonValueKind.Object &&
            meta.TryGetProperty("title", out var title) &&
            title.ValueKind == JsonValueKind.String)
        {
            return title.GetString();
        }

        return null;
    }

    private static void AddPrimitiveFieldFromTitle(
        string title,
        string nodeId,
        JsonElement nodeInputs,
        IDictionary<string, string> inputs,
        IDictionary<string, WorkflowInputMapping> mapping)
    {
        if (!HasLiteralField(nodeInputs, "value"))
        {
            return;
        }

        var normalizedTitle = title.Trim().ToLowerInvariant();
        if (normalizedTitle.Contains("fps", StringComparison.Ordinal))
        {
            AddField("fps", "float", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("duration", StringComparison.Ordinal))
        {
            AddField("duration", "float", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("frame", StringComparison.Ordinal) || normalizedTitle.Contains("length", StringComparison.Ordinal))
        {
            AddField("frameCount", "int", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("steps", StringComparison.Ordinal))
        {
            AddField("steps", "int", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("cfg", StringComparison.Ordinal))
        {
            AddField("cfg", "float", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("seed", StringComparison.Ordinal))
        {
            AddField("seed", "int", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("width", StringComparison.Ordinal))
        {
            AddField("width", "int", nodeId, "value", inputs, mapping);
        }

        if (normalizedTitle.Contains("height", StringComparison.Ordinal))
        {
            AddField("height", "int", nodeId, "value", inputs, mapping);
        }
    }

    private static void AddLiteralField(
        string fieldName,
        string fieldType,
        string nodeId,
        JsonElement nodeInputs,
        string targetField,
        IDictionary<string, string> inputs,
        IDictionary<string, WorkflowInputMapping> mapping)
    {
        if (!HasLiteralField(nodeInputs, targetField))
        {
            return;
        }

        AddField(fieldName, fieldType, nodeId, targetField, inputs, mapping);
    }

    private static void AddField(
        string fieldName,
        string fieldType,
        string nodeId,
        string targetField,
        IDictionary<string, string> inputs,
        IDictionary<string, WorkflowInputMapping> mapping)
    {
        if (inputs.ContainsKey(fieldName))
        {
            return;
        }

        inputs[fieldName] = fieldType;
        mapping[fieldName] = new WorkflowInputMapping
        {
            NodeId = nodeId,
            Field = targetField
        };
    }

    private static string NormalizeName(string value)
    {
        var sanitized = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(sanitized) ? "workflow-template" : sanitized;
    }
}
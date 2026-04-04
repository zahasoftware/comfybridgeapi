using System.Text.Json.Nodes;
using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
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

        var missingKeys = template.Inputs.Keys
            .Where(k => !body.ContainsKey(k))
            .ToArray();

        if (missingKeys.Length > 0)
        {
            throw new InputValidationException(
                $"Missing required field(s): {string.Join(", ", missingKeys)}.");
        }

        var safeInputs = new JsonObject();
        foreach (var key in template.Inputs.Keys)
        {
            safeInputs[key] = body[key]?.DeepClone();
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
}

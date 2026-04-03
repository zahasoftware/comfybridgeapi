using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using ComfyBridge.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/workflows")]
public sealed class WorkflowController(IWorkflowAnalyzer workflowAnalyzer, ITemplateService templateService) : ControllerBase
{
    [HttpGet("templates")]
    [ProducesResponseType(typeof(IEnumerable<TemplateResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TemplateResponse>>> ListTemplates(CancellationToken cancellationToken)
    {
        var templates = await templateService.GetTemplatesAsync(cancellationToken);
        return Ok(templates.Select(t => new TemplateResponse
        {
            Name = t.Name,
            Version = t.Version,
            Inputs = t.Inputs
        }));
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(WorkflowAnalyzeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowAnalyzeResponse>> Analyze([FromBody] WorkflowAnalyzeRequest request, CancellationToken cancellationToken)
    {
        ValidateWorkflowJson(request.WorkflowJson);

        var result = await workflowAnalyzer.AnalyzeAsync(request.WorkflowJson, cancellationToken);
        return Ok(new WorkflowAnalyzeResponse
        {
            Name = result.Template.Name,
            Version = result.Template.Version,
            Inputs = result.Template.Inputs,
            Mapping = result.Template.Mapping,
            ExampleRequest = result.ExampleRequest,
            DetectedNodeTypes = result.DetectedNodeTypes,
            Notes = result.Notes
        });
    }

    [HttpPost("save")]
    [ProducesResponseType(typeof(TemplateResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TemplateResponse>> Save([FromBody] WorkflowSaveRequest request, CancellationToken cancellationToken)
    {
        ValidateWorkflowJson(request.WorkflowJson);

        JsonNode workflowNode;
        try
        {
            workflowNode = JsonNode.Parse(request.WorkflowJson) ?? throw new InputValidationException("Workflow JSON is required.");
        }
        catch (JsonException ex)
        {
            throw new InputValidationException($"Workflow JSON is invalid: {ex.Message}");
        }

        var template = await templateService.SaveTemplateAsync(new WorkflowTemplate
        {
            Name = request.Name,
            Version = request.Version,
            Inputs = request.Inputs,
            Mapping = request.Mapping,
            Workflow = workflowNode
        }, cancellationToken);

        return Created($"/workflows/{Uri.EscapeDataString(template.TemplateKey)}", new TemplateResponse
        {
            Name = template.Name,
            Version = template.Version,
            Inputs = template.Inputs
        });
    }

    [HttpDelete("templates/{name}/{version}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string name, string version, CancellationToken cancellationToken)
    {
        await templateService.DeleteTemplateAsync(name, version, cancellationToken);
        return NoContent();
    }

    private static void ValidateWorkflowJson(string workflowJson)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            throw new InputValidationException("Workflow JSON is required.");
        }

        if (workflowJson.Length > 2_000_000)
        {
            throw new InputValidationException("Workflow JSON exceeds max allowed size (2 MB).");
        }

        try
        {
            using var document = JsonDocument.Parse(workflowJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InputValidationException("Workflow JSON must be an object.");
            }
        }
        catch (JsonException ex)
        {
            throw new InputValidationException($"Workflow JSON is invalid: {ex.Message}");
        }
    }
}
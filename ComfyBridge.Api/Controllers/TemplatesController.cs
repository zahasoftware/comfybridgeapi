using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/templates")]
public sealed class TemplatesController(ITemplateService templateService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TemplateResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TemplateResponse>>> GetTemplates(CancellationToken cancellationToken)
    {
        var templates = await templateService.GetTemplatesAsync(cancellationToken);
        var response = templates.Select(t => new TemplateResponse
        {
            Name = t.Name,
            Version = t.Version,
            Category = t.Category,
            Inputs = t.Inputs
        });

        return Ok(response);
    }
}
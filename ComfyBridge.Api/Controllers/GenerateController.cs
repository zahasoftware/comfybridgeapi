using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/generate")]
public sealed class GenerateController(IGenerationService generationService) : ControllerBase
{
    [HttpPost("image")]
    [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GenerateImage([FromBody] GenerateImageRequest request, CancellationToken cancellationToken)
    {
        var job = await generationService.StartImageGenerationAsync(request.Template, request.Inputs, cancellationToken);

        return Accepted(new GenerateResponse
        {
            JobId = job.JobId,
            Status = job.Status.ToString()
        });
    }

    [HttpPost("video")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GenerateVideo() =>
        StatusCode(StatusCodes.Status501NotImplemented, new { message = "Video generation endpoint is reserved for future workflows." });
}
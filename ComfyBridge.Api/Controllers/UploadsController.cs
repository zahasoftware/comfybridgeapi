using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Models;
using ComfyBridge.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/uploads")]
public sealed class UploadsController(
    IComfyClient comfyClient,
    IOptions<ComfyUiOptions> comfyOptions) : ControllerBase
{
    [HttpPost("image")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ImageUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImageUploadResponse>> UploadImage(
        [FromForm] IFormFile file,
        [FromForm] string? type,
        [FromForm] string? subfolder,
        [FromForm] bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Image file is required.");
        }

        var options = comfyOptions.Value;
        if (file.Length > options.UploadMaxFileSizeBytes)
        {
            return BadRequest($"File size exceeds max allowed ({options.UploadMaxFileSizeBytes} bytes).");
        }

        var extension = Path.GetExtension(file.FileName);
        var allowedExtensions = options.AllowedUploadExtensions ?? [];
        if (allowedExtensions.Length > 0 &&
            !allowedExtensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest($"File extension '{extension}' is not allowed. Allowed: {string.Join(", ", allowedExtensions)}.");
        }

        await using var stream = file.OpenReadStream();
        var result = await comfyClient.UploadImageAsync(stream, new ComfyImageUploadRequest
        {
            FileName = file.FileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Type = string.IsNullOrWhiteSpace(type) ? "input" : type,
            Subfolder = subfolder,
            Overwrite = overwrite
        }, cancellationToken);

        return Ok(new ImageUploadResponse
        {
            ComfyFileName = result.ComfyFileName,
            Subfolder = result.Subfolder,
            Type = result.Type,
            ViewUrl = result.ViewUrl
        });
    }
}

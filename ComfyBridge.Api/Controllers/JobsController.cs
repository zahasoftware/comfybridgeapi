using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
public sealed class JobsController(IJobService jobService, IComfyClient comfyClient) : ControllerBase
{
    [HttpGet("{jobId}")]
    [ProducesResponseType(typeof(JobResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<JobResponse>> GetJob([FromRoute] string jobId, CancellationToken cancellationToken)
    {
        var job = await jobService.GetJobAsync(jobId, cancellationToken);
        return Ok(new JobResponse
        {
            JobId = job.JobId,
            Template = job.TemplateName,
            Version = job.TemplateVersion,
            Status = job.Status.ToString(),
            ExternalExecutionId = job.ExternalExecutionId,
            ErrorMessage = job.ErrorMessage,
            Result = job.Result?.RawResult,
            AssetUrls = job.Result?.AssetUrls,
            CreatedAtUtc = job.CreatedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc
        });
    }

    [HttpGet("{jobId}/outputs/{assetIndex:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadOutput([FromRoute] string jobId, [FromRoute] int assetIndex, CancellationToken cancellationToken)
    {
        if (assetIndex < 0)
        {
            throw new InputValidationException("assetIndex must be zero or greater.");
        }

        var job = await jobService.GetJobAsync(jobId, cancellationToken);
        var assetUrls = job.Result?.AssetUrls;

        if (assetUrls is null || assetUrls.Count == 0)
        {
            throw new InputValidationException("This job has no downloadable outputs yet.");
        }

        if (assetIndex >= assetUrls.Count)
        {
            throw new InputValidationException($"assetIndex '{assetIndex}' is out of range. Available outputs: 0 to {assetUrls.Count - 1}.");
        }

        var downloadedAsset = await comfyClient.DownloadAssetAsync(assetUrls[assetIndex], cancellationToken);
        return File(downloadedAsset.Content, downloadedAsset.ContentType, downloadedAsset.FileName);
    }
}
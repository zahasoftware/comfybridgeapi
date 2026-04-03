using ComfyBridge.Api.Contracts;
using ComfyBridge.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ComfyBridge.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
public sealed class JobsController(IJobService jobService) : ControllerBase
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
}
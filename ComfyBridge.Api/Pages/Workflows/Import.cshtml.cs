using System.Text;
using System.Text.Json;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages.Workflows;

public sealed class ImportModel(IWorkflowAnalyzer analyzer, IWorkflowDraftStore draftStore, ILogger<ImportModel> logger) : PageModel
{
    private const int MaxFileSizeBytes = 1_000_000;

    [BindProperty]
    public IFormFile? WorkflowFile { get; set; }

    [BindProperty]
    public string? WorkflowJson { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnPostAnalyzeAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = new System.Collections.Generic.List<string>();
            foreach (var modelState in ModelState.Values)
            {
                foreach (var error in modelState.Errors)
                {
                    errors.Add(error.ErrorMessage ?? error.Exception?.Message ?? "Unknown error");
                }
            }
            var errorSummary = string.Join("; ", errors);
            ErrorMessage = $"Form validation failed: {(string.IsNullOrWhiteSpace(errorSummary) ? "Unknown error" : errorSummary)}";
            System.Console.WriteLine($"[WORKFLOW IMPORT] ModelState validation failed: {ErrorMessage}");
            return Page();
        }
        
        System.Console.WriteLine($"[WORKFLOW IMPORT] OnPostAnalyzeAsync called. WorkflowFile={WorkflowFile}, WorkflowJson length={WorkflowJson?.Length ?? 0}");

        string payload;
        try
        {
            payload = await ReadPayloadAsync(cancellationToken);
        }
        catch (InputValidationException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error reading workflow: {ex.Message}";
            return Page();
        }

        try
        {
            var result = await analyzer.AnalyzeAsync(payload, cancellationToken);
            var draftId = draftStore.SaveDraft(result);
            logger.LogInformation("[IMPORT] Successfully analyzed workflow. Generated {InputCount} inputs, {MappingCount} mappings. Draft ID: {DraftId}",
                result.Template.Inputs.Count,
                result.Template.Mapping.Count,
                draftId);
            return Redirect($"/workflows/{Uri.EscapeDataString(draftId)}?draft=true");
        }
        catch (InputValidationException ex)
        {
            logger.LogWarning("[IMPORT] Input validation error: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            WorkflowJson = payload;
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[IMPORT] Unexpected error during analysis");
            ErrorMessage = $"Analysis failed: {ex.Message}";
            WorkflowJson = payload;
            return Page();
        }
    }

    private async Task<string> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        if (WorkflowFile is not null)
        {
            logger.LogInformation("[IMPORT] Processing file upload: {FileName}, size: {Size} bytes", WorkflowFile.FileName, WorkflowFile.Length);
            
            if (WorkflowFile.Length == 0)
            {
                logger.LogWarning("[IMPORT] Uploaded file is empty");
                throw new InputValidationException("Uploaded file is empty.");
            }

            if (WorkflowFile.Length > MaxFileSizeBytes)
            {
                logger.LogWarning("[IMPORT] File size {Size} exceeds limit {Max}", WorkflowFile.Length, MaxFileSizeBytes);
                throw new InputValidationException("File size exceeds 1 MB limit.");
            }

            await using var stream = WorkflowFile.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            var fromFile = await reader.ReadToEndAsync(cancellationToken);
            ValidateJson(fromFile);
            logger.LogInformation("[IMPORT] File upload validated successfully, size: {Size} bytes", fromFile.Length);
            return fromFile;
        }

        if (string.IsNullOrWhiteSpace(WorkflowJson))
        {
            logger.LogWarning("[IMPORT] No workflow file or JSON provided");
            throw new InputValidationException("Upload a workflow file or paste JSON to continue.");
        }

        logger.LogInformation("[IMPORT] Processing pasted JSON, size: {Size} bytes", WorkflowJson.Length);
        
        if (WorkflowJson.Length > MaxFileSizeBytes)
        {
            logger.LogWarning("[IMPORT] JSON size {Size} exceeds limit {Max}", WorkflowJson.Length, MaxFileSizeBytes);
            throw new InputValidationException("JSON exceeds 1 MB limit.");
        }

        ValidateJson(WorkflowJson);
        logger.LogInformation("[IMPORT] Pasted JSON validated successfully");
        return WorkflowJson;
    }

    private void ValidateJson(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("[IMPORT] JSON root is not an object, type: {Type}", doc.RootElement.ValueKind);
                throw new InputValidationException("Workflow JSON must be an object.");
            }
            logger.LogDebug("[IMPORT] JSON validation passed");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[IMPORT] JSON parsing error: {Message}", ex.Message);
            throw new InputValidationException($"Workflow JSON is invalid: {ex.Message}");
        }
    }
}
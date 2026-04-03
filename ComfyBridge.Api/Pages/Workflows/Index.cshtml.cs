using ComfyBridge.Domain.Models;
using ComfyBridge.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComfyBridge.Api.Pages.Workflows;

public sealed class IndexModel(ITemplateService templateService) : PageModel
{
    public IReadOnlyCollection<WorkflowTemplate> Templates { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Templates = await templateService.GetTemplatesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name, string version, CancellationToken cancellationToken)
    {
        await templateService.DeleteTemplateAsync(name, version, cancellationToken);
        StatusMessage = $"Template '{name}:{version}' deleted.";
        return RedirectToPage("/Workflows/Index");
    }
}
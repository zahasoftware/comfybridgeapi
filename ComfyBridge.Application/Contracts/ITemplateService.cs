using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface ITemplateService
{
    Task<IReadOnlyCollection<WorkflowTemplate>> GetTemplatesAsync(CancellationToken cancellationToken);

    Task<WorkflowTemplate> GetTemplateAsync(string name, string? version, CancellationToken cancellationToken);

    Task<WorkflowTemplate> SaveTemplateAsync(WorkflowTemplate template, CancellationToken cancellationToken);

    Task DeleteTemplateAsync(string name, string version, CancellationToken cancellationToken);
}
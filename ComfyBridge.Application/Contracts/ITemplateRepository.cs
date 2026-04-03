using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface ITemplateRepository
{
    Task<IReadOnlyCollection<WorkflowTemplate>> GetAllTemplatesAsync(CancellationToken cancellationToken);

    Task<WorkflowTemplate?> GetTemplateAsync(string name, string? version, CancellationToken cancellationToken);

    Task SaveTemplateAsync(WorkflowTemplate template, CancellationToken cancellationToken);

    Task DeleteTemplateAsync(string name, string version, CancellationToken cancellationToken);
}
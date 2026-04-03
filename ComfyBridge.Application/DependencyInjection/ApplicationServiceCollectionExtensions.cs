using ComfyBridge.Application.Contracts;
using ComfyBridge.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ComfyBridge.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<IWorkflowInjectionService, WorkflowInjectionService>();
        services.AddScoped<IWorkflowAnalyzer, WorkflowAnalyzerService>();
        services.AddSingleton<IWorkflowDraftStore, InMemoryWorkflowDraftStore>();
        services.AddSingleton<IJobService, JobService>();
        services.AddSingleton<IGenerationService, GenerationService>();

        return services;
    }
}
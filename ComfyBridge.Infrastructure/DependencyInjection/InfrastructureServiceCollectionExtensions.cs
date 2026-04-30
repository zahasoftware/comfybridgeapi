using ComfyBridge.Application.Contracts;
using ComfyBridge.Infrastructure.Ai;
using ComfyBridge.Infrastructure.Comfy;
using ComfyBridge.Infrastructure.Options;
using ComfyBridge.Infrastructure.Queue;
using ComfyBridge.Infrastructure.Stores;
using ComfyBridge.Infrastructure.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComfyBridge.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ComfyUiOptions>(configuration.GetSection(ComfyUiOptions.SectionName));
        services.Configure<TemplateStorageOptions>(configuration.GetSection(TemplateStorageOptions.SectionName));
        services.Configure<JobStoreOptions>(configuration.GetSection(JobStoreOptions.SectionName));
        services.Configure<WorkflowAiOptions>(configuration.GetSection(WorkflowAiOptions.SectionName));

        services.AddSingleton<ITemplateRepository, FileTemplateRepository>();
        services.AddSingleton<IGenerationJobQueue, InMemoryGenerationJobQueue>();

        var provider = configuration.GetSection(JobStoreOptions.SectionName)["Provider"] ?? "InMemory";
        if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJobStore, RedisJobStore>();
        }
        else
        {
            services.AddSingleton<IJobStore, InMemoryJobStore>();
        }

        services.AddHttpClient<IComfyClient, ComfyClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ComfyUiOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            // The generation timeout is controlled explicitly in ComfyClient via JobTimeoutSeconds.
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddHttpClient<IAiClient, AiWorkflowClient>();

        return services;
    }
}
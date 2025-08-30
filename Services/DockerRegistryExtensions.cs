using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetDockerRegistry;

public static class DockerRegistryExtensions
{
    public static IServiceCollection AddDockerRegistry(this IServiceCollection services)
    {
        services.AddSingleton<DockerRegistry>();
        services.AddSingleton<DockerRegistryApi>();

        return services;
    }

    public static WebApplication UseDockerRegistry(this WebApplication application)
    {
        var registry = application.Services.GetRequiredService<DockerRegistryApi>();

        registry.SetupRoutes(application);

        return application;
    }
}
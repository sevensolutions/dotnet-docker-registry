using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetDockerRegistry.Services;

public static class DockerRegistryExtensions
{
    public static IServiceCollection AddDockerRegistry(this IServiceCollection services)
    {
        services.AddSingleton<SessionStorage>();
        services.AddSingleton<DockerRegistry>();
        services.AddSingleton<DockerRegistryApi>();

        services.AddHostedService(sp => sp.GetRequiredService<DockerRegistry>());

        return services;
    }

    public static WebApplication UseDockerRegistry(this WebApplication application)
    {
        var registry = application.Services.GetRequiredService<DockerRegistryApi>();

        registry.SetupRoutes(application);

        return application;
    }
}
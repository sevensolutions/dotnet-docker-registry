using System.Threading.Tasks;
using DotNetDockerRegistry.Core;
using DotNetDockerRegistry.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DotNetDockerRegistry.Services;

public sealed class WwwAuthenticateMiddleware : IMiddleware
{
    private readonly string _serverUrl;

    public WwwAuthenticateMiddleware(IOptions<DockerRegistryOptions> options)
    {
        _serverUrl = options.Value.ServerUrl;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = $"Bearer realm=\"{_serverUrl}/token\",scope=\"repository:samalba/my-app:pull,push";

            await context.Response.WriteAsJsonAsync(new DockerApiErrors()
            {
                Errors = [new DockerApiError()
                {
                    Code = DockerErrorCodes.UNAUTHORIZED,
                    Message = "access to the requested resource is not authorized"
                }]
            });
        }
    }
}
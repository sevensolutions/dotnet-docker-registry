using DotNetDockerRegistry.Options;
using DotNetDockerRegistry.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerRegistryOptions>(builder.Configuration.GetSection("Registry"));

builder.Services.AddDistributedMemoryCache();

builder.Services.AddDockerRegistry();

builder.Services.AddSingleton<WwwAuthenticateMiddleware>();

builder.Services.AddSingleton<IDockerIdentityProvider, DockerIdentityProvider>();

builder.Services
    .AddAuthentication()
    .AddDockerIdentity();

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<WwwAuthenticateMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// TODO: If docker client is unauthenticated, it issues a GET here for some reason...
app.MapPost("token", async (HttpContext context, [FromServices] IDockerIdentityProvider tokenProvider) =>
{
    var form = await context.Request.ReadFormAsync();

    var token = await tokenProvider.CreateTokenAsync("test");

    return Results.Ok(new
    {
        token = token
    });
}).AllowAnonymous();

app.UseDockerRegistry()
    .RequireAuthorization();

app.Run();

using DotNetDockerRegistry;
using DotNetDockerRegistry.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerRegistryOptions>(builder.Configuration.GetSection("Registry"));

builder.Services.AddDistributedMemoryCache();

builder.Services.AddDockerRegistry();

var app = builder.Build();

app.UseDockerRegistry();

app.Run();

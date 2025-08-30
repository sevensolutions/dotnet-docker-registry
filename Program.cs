using DotNetDockerRegistry;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDockerRegistry();

var app = builder.Build();

app.UseDockerRegistry();

app.Run();

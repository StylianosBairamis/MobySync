using MobySync.Extensions;
using MobySync.Filters;
using MobySync.Helpers;
using MobySync.Models;
using MobySync.Services;

var builder = WebApplication.CreateBuilder(args);

// Register Services
builder.Services.AddSingleton<DockerHelper>();
builder.Services.AddSingleton<UpdateCoordinator>();
builder.Services.AddSingleton<CredentialsHelper>();

builder.Services.AddHostedService<UpdateService>();

var app = builder.Build();

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("API_KEY")))
{
    app.Logger.LogCritical("API_KEY environment variable is not set, exiting...");
    
    Environment.Exit(1);
}

var apiSection = app.Configuration.GetSection(nameof(ApiSection)).Get<ApiSection>();

if (apiSection is null || string.IsNullOrEmpty(apiSection.Ip) || string.IsNullOrEmpty(apiSection.Port) || string.IsNullOrEmpty(apiSection.Scheme))
{
    app.Logger.LogCritical("ApiSection is missing or corrupted in appsettings.json, exiting...");
    
    Environment.Exit(1);
}

app.MapGroup("/api")
   .AddEndpointFilter<ApiKeyFilter>()
   .MapUpdaterEndpoints();

app.Run($"{apiSection.Scheme}://{apiSection.Ip}:{apiSection.Port}");

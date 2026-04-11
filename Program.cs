using docker_image_updater.Extensions;
using docker_image_updater.Filters;
using docker_image_updater.Helpers;
using docker_image_updater.Models;
using docker_image_updater.Services;

var builder = WebApplication.CreateBuilder(args);

// Register Services
builder.Services.AddSingleton<ConfigurationHelper>();
builder.Services.AddSingleton(service => service.GetRequiredService<ConfigurationHelper>().GetConfiguration());
builder.Services.AddSingleton<DockerHelper>();
builder.Services.AddSingleton<UpdateCoordinator>();
builder.Services.AddSingleton<CredentialsHelper>();

builder.Services.AddHostedService<UpdateService>();

var app = builder.Build();

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("API_KEY")))
{
    app.Logger.LogCritical("API_KEY environment variable is not set. Exiting...");
    
    Environment.Exit(1);
}

var apiSection = app.Configuration.GetSection(nameof(ApiSection)).Get<ApiSection>();

if (apiSection is null || string.IsNullOrEmpty(apiSection.Ip) || string.IsNullOrEmpty(apiSection.Port) || string.IsNullOrEmpty(apiSection.Scheme))
{
    app.Logger.LogCritical("ApiSection is missing or corrupted in appsettings.json. Exiting...");
    
    Environment.Exit(1);
}

app.MapGroup("/api")
   .AddEndpointFilter<ApiKeyFilter>()
   .MapUpdaterEndpoints();

app.Run($"{apiSection.Scheme}://{apiSection.Ip}:{apiSection.Port}");

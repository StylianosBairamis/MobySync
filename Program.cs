using System.Text.Json;
using docker_image_updater;
using docker_image_updater.Data.Models;
using docker_image_updater.Helpers;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var apiSection = builder.Configuration.GetRequiredSection(nameof(ApiSection))
                                .Get<ApiSection>();

if (apiSection is null)
{
    Console.WriteLine($"ApiSection section is missing or corrupted, exiting...");

    Environment.Exit(1);
}

if (string.IsNullOrEmpty(apiSection.Ip) || string.IsNullOrEmpty(apiSection.Port) || string.IsNullOrEmpty(apiSection.Scheme))
{
    Console.WriteLine("One or more ApiSection properties are missing or corrupted, exiting...");

    Environment.Exit(1);
}

// Remove this in production, it will be mounted
var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

var updateConfigurationPath = Path.Combine(baseDirectory, "updater-configuration.json");

if (!File.Exists(updateConfigurationPath))
{
    Console.WriteLine($"[!] Configuration file not found at {updateConfigurationPath}");
    
    Environment.Exit(1);
}

try
{
    var jsonString = await File.ReadAllTextAsync(updateConfigurationPath);

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    var config = JsonSerializer.Deserialize<UpdaterConfiguration>(jsonString, options);
    
    builder.Services.AddSingleton<UpdaterConfiguration>(config);
}
catch (JsonException ex)
{
    Console.WriteLine($"[!] Failed to parse JSON configuration: {ex.Message}");
    
    Environment.Exit(1);
}

//Register Services
builder.Services.AddSingleton<DockerHelper>();
builder.Services.AddSingleton<UpdateCoordinator>();

builder.Services.AddHostedService<UpdateService>();

var app = builder.Build();

app.MapPost("/api/update/trigger", async ([FromHeader(Name = "X-Api-Key")] string apiKey, UpdateCoordinator updateCoordinator) =>
{
    if (apiKey != apiSection.ApiKey) 
    {
        return Results.Unauthorized();
    }
    
    var started = await updateCoordinator.TryStartManualUpdate(); 
    
    return started 
        ? Results.Accepted() 
        : Results.Conflict(new { status = "An update process is alreday running" });
});

app.Run($"{apiSection.Scheme}://{apiSection.Ip}:{apiSection.Port}");


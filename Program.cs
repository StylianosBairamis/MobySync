using System.Text.Json;
using docker_image_updater;
using docker_image_updater.Data.Models;
using docker_image_updater.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DockerHelper>();
builder.Services.AddSingleton<TransactionHelper>();

var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

var updateConfigurationPath = Path.Combine(baseDirectory, "updater-configuration.json");

if (!File.Exists(updateConfigurationPath))
{
    Console.WriteLine($"[!] Configuration file not found at {updateConfigurationPath}");
    
    return;
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
    
    return;
}

builder.Services.AddHostedService<UpdateService>();

var app = builder.Build();


app.MapGet("/test", (DockerHelper dockerHelper) =>
{
   
});

var dockerHelper = app.Services.GetService<DockerHelper>();

// await dockerHelper.Test();

app.Run();


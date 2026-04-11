using docker_image_updater.Data.Models;
using docker_image_updater.Dtos;
using docker_image_updater.Helpers;
using docker_image_updater.Services;
using Microsoft.AspNetCore.Mvc;

namespace docker_image_updater.Extensions;

public static class RouteExtensions
{
    public static void MapUpdaterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/update/trigger", async (UpdateCoordinator updateCoordinator) =>
        {
            var started = await updateCoordinator.TryStartManualUpdate(); 
            
            return started 
                ? Results.Accepted() 
                : Results.Conflict(new { status = "An update process is already running" });
        });

        endpoints.MapPut("/configuration", async ([FromBody] UpdateConfigurationDto updateConfigDto, IConfigurationService configService) =>
        {
            var currentConfig = configService.GetConfiguration();
            
            var newConfig = new UpdaterConfiguration
            {
                Services = updateConfigDto.Services ?? currentConfig.Services,
                GenericSettings = updateConfigDto.GenericSettings ?? currentConfig.GenericSettings
            };

            if (!configService.Validate(newConfig, out string errorMessage))
            {
                return Results.BadRequest(new { error = errorMessage });
            }

            await configService.UpdateConfiguration(newConfig);
            
            return Results.Ok(newConfig);
        });

        endpoints.MapGet("/configuration", (IConfigurationService configService) => 
        {
            return Results.Ok(configService.GetConfiguration());
        });
    }
}

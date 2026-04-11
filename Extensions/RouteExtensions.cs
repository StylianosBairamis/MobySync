using MobySync.Dtos;
using MobySync.Helpers;
using MobySync.Models;
using Microsoft.AspNetCore.Mvc;

namespace MobySync.Extensions;

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

        endpoints.MapPut("/configuration", async ([FromBody] UpdateConfigurationDto updateConfigDto, ConfigurationHelper configurationHelper) =>
        {
            var currentConfig = configurationHelper.GetConfiguration();
            
            var newConfig = new UpdaterConfiguration
            {
                Services = updateConfigDto.Services ?? currentConfig.Services,
                GenericSettings = updateConfigDto.GenericSettings ?? currentConfig.GenericSettings
            };

            if (!configurationHelper.Validate(newConfig, out string errorMessage))
            {
                return Results.BadRequest(new { error = errorMessage });
            }

            await configurationHelper.UpdateConfiguration(newConfig);
            
            return Results.Ok(newConfig);
        });

        endpoints.MapGet("/configuration", (ConfigurationHelper configService) => 
        {
            return Results.Ok(configService.GetConfiguration());
        });
    }
}

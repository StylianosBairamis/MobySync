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
    }
}

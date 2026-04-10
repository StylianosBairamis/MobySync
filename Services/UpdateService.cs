using docker_image_updater.Data.Models;
using docker_image_updater.Helpers;

namespace docker_image_updater;

public class UpdateService(UpdaterConfiguration updaterConfiguration, UpdateCoordinator updateCoordinator  , ILogger<UpdateService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;

            var scheduledTimeUtc = nowUtc.Date.AddHours(updaterConfiguration.GenericSettings.Hour)
                                    .AddMinutes(updaterConfiguration.GenericSettings.Minute);
        
            if (nowUtc > scheduledTimeUtc)
            {
                scheduledTimeUtc = scheduledTimeUtc.AddDays(1);
            }
            
            logger.LogInformation("Next scan for updates scheduled at: {Time}", scheduledTimeUtc);
            
            var delay = scheduledTimeUtc - nowUtc;
            
            await Task.Delay(delay, cancellationToken);

            await updateCoordinator.ExecuteScheduledUpdate();
        }
    }
}
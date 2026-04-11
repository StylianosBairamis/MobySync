using MobySync.Helpers;
using MobySync.Models;

namespace MobySync.Services;

public class UpdateService(UpdaterConfiguration updaterConfiguration, UpdateCoordinator updateCoordinator, ILogger<UpdateService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;

            var scheduledTimeUtc = nowUtc.Date.AddHours(updaterConfiguration.GenericSettings.Hour)
                                    .AddMinutes(updaterConfiguration.GenericSettings.Minute);
        
            // If the set time is past the current, schedule it for next day.
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
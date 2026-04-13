using MobySync.Helpers;
using MobySync.Models;

namespace MobySync.Services;

public class UpdateService(ConfigurationHelper configurationHelper, UpdateCoordinator updateCoordinator, ILogger<UpdateService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var timezone = Environment.GetEnvironmentVariable("TZ");
        
        if (string.IsNullOrWhiteSpace(timezone))
        {
            logger.LogWarning("Timezone variable is not set, the update schedule will default to the container's native timezone.");
        }
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var updaterConfiguration = configurationHelper.GetConfiguration();
            
            var dateTimeNow = DateTime.Now;

            var scheduledTimeUtc = dateTimeNow.Date.AddHours(updaterConfiguration.GenericSettings.Hour)
                                    .AddMinutes(updaterConfiguration.GenericSettings.Minute);
        
            // If the set time is past the current, schedule it for next day.
            if (dateTimeNow > scheduledTimeUtc)
            {
                scheduledTimeUtc = scheduledTimeUtc.AddDays(1);
            }
            
            logger.LogInformation("Next scan for updates scheduled at: {Time}", scheduledTimeUtc);
            
            var delay = scheduledTimeUtc - dateTimeNow;

            var scheduleChanged = await configurationHelper.ConfigChangedSignal.WaitAsync(delay, cancellationToken);

            if (scheduleChanged)
            {
                logger.LogInformation("Configuration changed, recalculating update schedule");
                
                continue;
            }

            await updateCoordinator.ExecuteScheduledUpdate();
        }
    }
}
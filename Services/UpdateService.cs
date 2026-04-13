using MobySync.Helpers;

namespace MobySync.Services;

public class UpdateService(UpdateCoordinator updateCoordinator, ILogger<UpdateService> logger) : BackgroundService
{
    private int _updateHour = 23;
    
    private int _updateMinute = 30;
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var timezone = Environment.GetEnvironmentVariable("TZ");
        
        if(int.TryParse(Environment.GetEnvironmentVariable("UPDATE_HOUR"), out var hourParsed))
        {
            logger.LogInformation("UPDATE_HOUR variable is not set. Defaulting update hour to {Hour}", _updateHour);
        }
        
        if(int.TryParse(Environment.GetEnvironmentVariable("UPDATE_MINUTE"), out var minuteParsed))
        {
            logger.LogInformation("UPDATE_MINUTE variable is not set. Defaulting update minute to {Minute}", _updateMinute);
        }
        
        if (string.IsNullOrWhiteSpace(timezone))
        {
            logger.LogWarning("Timezone variable is not set, the update schedule will default to the container's native timezone.");
        }
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var dateTimeNow = DateTime.Now;

            var scheduledTime = dateTimeNow.Date.AddHours(_updateHour)
                                    .AddMinutes(_updateMinute);
        
            // If the set time is past the current, schedule it for next day.
            if (dateTimeNow > scheduledTime)
            {
                scheduledTime = scheduledTime.AddDays(1);
            }
            
            logger.LogInformation("Next scan for updates scheduled at: {Time}", scheduledTime);
            
            var delay = scheduledTime - dateTimeNow;

            await Task.Delay(delay, cancellationToken);

            await updateCoordinator.ExecuteScheduledUpdate();
        }
    }
}
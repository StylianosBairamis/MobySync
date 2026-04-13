using MobySync.Helpers;

namespace MobySync.Services;

public class UpdateService(UpdateCoordinator updateCoordinator, ILogger<UpdateService> logger) : BackgroundService
{
    private int _updateHour = 23;
    
    private int _updateMinute = 30;
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("UPDATE_HOUR"), out var hourParsed))
        {
            _updateHour = hourParsed; 
        }
        else
        {
            logger.LogInformation("UPDATE_HOUR variable is missing or invalid. Defaulting update hour to {Hour}", _updateHour);
        }
    
        if (int.TryParse(Environment.GetEnvironmentVariable("UPDATE_MINUTE"), out var minuteParsed))
        {
            _updateMinute = minuteParsed; 
        }
        else
        {
            logger.LogInformation("UPDATE_MINUTE variable is missing or invalid. Defaulting update minute to {Minute}", _updateMinute);
        }

        logger.LogInformation("Daily updates are scheduled to run at {Hour:00}:{Minute:00} container time.",
            _updateHour, _updateMinute);
            
        var timezone = Environment.GetEnvironmentVariable("TZ");

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
            
            var delay = scheduledTime - dateTimeNow;

            await Task.Delay(delay, cancellationToken);

            await updateCoordinator.ExecuteScheduledUpdate();
        }
    }
}
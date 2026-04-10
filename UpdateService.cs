using docker_image_updater.Data.Models;
using docker_image_updater.Helpers;
using Docker.DotNet;

namespace docker_image_updater;

public class UpdateService(UpdaterConfiguration config, DockerHelper dockerHelper, ILogger<UpdateService> logger, TransactionHelper transactionHelper) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Performing check for unapplied image updates. ");

        while (!cancellationToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;

            var scheduledTimeUtc = nowUtc.Date.AddHours(config.GenericSettings.Hour)
                                    .AddMinutes(config.GenericSettings.Minute);
        
            if (nowUtc > scheduledTimeUtc)
            {
                scheduledTimeUtc = scheduledTimeUtc.AddDays(1);
            }

            var delay = scheduledTimeUtc - nowUtc;
            
            // await Task.Delay(delay, cancellationToken);

            await dockerHelper.CheckForImageUpdates();
        }
    }
}
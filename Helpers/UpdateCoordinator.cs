using MobySync.Interfaces;

namespace MobySync.Helpers;

public class UpdateCoordinator(DockerHelper dockerHelper, IEnumerable<INotificationHelper> notificationHelpers, ILogger<UpdateCoordinator> logger)
{
    private readonly SemaphoreSlim _updateProcessLock = new(1, 1);
    
    private async Task StartUpdateCycle()
    {
        var summary = await dockerHelper.CheckForImageUpdates();

        if (summary.HasChanges)
        {
            foreach (var notificationHelper in notificationHelpers.Where(helper => helper.IsConfigured))
            {
                try
                {
                    await notificationHelper.SendSummary(summary);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An unexpected error occurred while trying to send a notification via provider: " +
                                        "{Provider}", notificationHelper.ProviderName);
                }
            }
        }
    }

    public async Task<bool> TryStartManualUpdate()
    {
        if (!await _updateProcessLock.WaitAsync(0))
        {
            return false;
        }
        
        _ = Task.Run(async () =>
        {
            try
            {
                await StartUpdateCycle();
            }
            finally
            {
                _updateProcessLock.Release();
            }
        });
        
        return true;
    }
    
    public async Task ExecuteScheduledUpdate()
    {
        await _updateProcessLock.WaitAsync(); 
        
        try
        {
            await StartUpdateCycle();
        }
        finally
        {
            _updateProcessLock.Release();
        }
    }
}
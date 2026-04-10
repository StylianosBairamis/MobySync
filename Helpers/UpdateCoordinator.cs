namespace docker_image_updater.Helpers;

public class UpdateCoordinator(DockerHelper dockerHelper, ILogger<UpdateCoordinator> logger)
{
    private readonly SemaphoreSlim _updateProcessLock = new SemaphoreSlim(1, 1);
    
    private async Task StartUpdateCycle()
    {
        try
        {
            await dockerHelper.CheckForImageUpdates();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A critical error occurred during the update cycle.");
        }
        finally
        {
            _updateProcessLock.Release();
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
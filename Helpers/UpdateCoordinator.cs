namespace docker_image_updater.Helpers;

public class UpdateCoordinator(DockerHelper dockerHelper, ILogger<UpdateCoordinator> logger)
{
    private readonly SemaphoreSlim _updateProcessLock = new(1, 1);
    
    private async Task StartUpdateCycle()
    {
        await dockerHelper.CheckForImageUpdates();
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
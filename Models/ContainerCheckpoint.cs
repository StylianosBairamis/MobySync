namespace MobySync.Models;

public class ContainerCheckpoint
{
    public string OldContainerId {get; init;} = string.Empty;   
    public string NewContainerId {get; init;} = string.Empty; 
    public string ContainerOriginalName {get; init;} = string.Empty; 
    public string ContainerBackupName {get; init;} = string.Empty; 
}
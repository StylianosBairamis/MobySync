namespace MobySync.Models;

public class ContainerUpdateResult
{
    public string ContainerName { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string OldTag { get; set; } = string.Empty;
    public string NewTag { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool Success { get; set; }
}
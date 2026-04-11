namespace MobySync.Models;

public class ServiceConfiguration
{
    public string ImageName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string TargetTag { get; set; } =  string.Empty;
}
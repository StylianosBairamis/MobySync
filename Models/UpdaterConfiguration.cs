namespace MobySync.Models;

public class UpdaterConfiguration
{
    public GenericSettings GenericSettings { get; set; } = new GenericSettings();
    public List<ServiceConfiguration> Services { get; set; } = new List<ServiceConfiguration>();
}
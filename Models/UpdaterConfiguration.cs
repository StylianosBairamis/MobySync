namespace docker_image_updater.Data.Models;

public class UpdaterConfiguration
{
    public GenericSettings GenericSettings { get; set; }
    public List<ServiceConfiguration> Services { get; set; } = new List<ServiceConfiguration>();
}
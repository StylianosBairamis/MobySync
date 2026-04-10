namespace docker_image_updater.Data.Models;

public class GenericSettings
{
    public int Hour { get; set; }
    
    public int Minute { get; set; }
    
    public bool PruneOldImages { get; set; }
}
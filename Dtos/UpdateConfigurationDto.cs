using docker_image_updater.Data.Models;

namespace docker_image_updater.Dtos;

public class UpdateConfigurationDto
{
    public List<ServiceConfiguration>? Services { get; set; }
    public GenericSettings? GenericSettings { get; set; }
}

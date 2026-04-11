using docker_image_updater.Data.Models;

namespace docker_image_updater.Services;

public interface IConfigurationService
{
    UpdaterConfiguration GetConfiguration();
    Task UpdateConfiguration(UpdaterConfiguration updaterConfiguration);
    bool Validate(UpdaterConfiguration config, out string errorMessage);
}

namespace docker_image_updater.Models;

public class RegistryCredentials
{
    public Dictionary<string, RegistryAuth> Auths { get; set; } = new();
}
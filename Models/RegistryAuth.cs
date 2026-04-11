using System.Text.Json.Serialization;

namespace docker_image_updater.Models;

public class RegistryAuth
{
    [JsonPropertyName("auth")]
    public string Auth { get; set; }
}
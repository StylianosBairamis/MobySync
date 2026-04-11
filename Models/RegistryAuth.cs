using System.Text.Json.Serialization;

namespace MobySync.Models;

public class RegistryAuth
{
    [JsonPropertyName("auth")]
    public string Auth { get; set; }
}
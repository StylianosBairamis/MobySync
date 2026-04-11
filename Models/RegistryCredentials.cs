namespace MobySync.Models;

public class RegistryCredentials
{
    public Dictionary<string, RegistryAuth> Auths { get; set; } = new();
}
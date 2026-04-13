using System.Text;
using System.Text.Json;
using MobySync.Models;
using Docker.DotNet.Models;

namespace MobySync.Helpers;

public class CredentialsHelper(ILogger<CredentialsHelper> logger)
{
    private readonly string _credentialsFileName = "config.json";

    private readonly string _credentialsDirectory = "creds";
    
    public async Task<AuthConfig> FetchCredentials(string baseImageName)
    {
        var registryLoginUrl = "https://index.docker.io/v1/"; 
        
        var registryServerAddress = "docker.io";

        var parts = baseImageName.Split('/');
        
        // Registry detection
        if (parts.Length > 1 && (parts[0].Contains('.') || parts[0].Contains(':')))
        {
            registryLoginUrl = TryGetLoginUrlForRegistry(parts[0]); 
            
            registryServerAddress = parts[0];
        }
        
        var dockerConfigPath = Path.Combine("/app", _credentialsDirectory, _credentialsFileName);

        if (!File.Exists(dockerConfigPath))
        {
            logger.LogWarning("No credentials file could be fetched, proceeding anonymously");
            
            return new AuthConfig();
        }
        
        return await ParseCredentialsFile(dockerConfigPath, registryLoginUrl, registryServerAddress);
    }

    private async Task<AuthConfig> ParseCredentialsFile(string credentialsFilePath, string registryLoginUrl, string registryServerAddress)
    {
        try
        {
            var jsonString = await File.ReadAllTextAsync(credentialsFilePath);

            var serializationOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            var config = JsonSerializer.Deserialize<RegistryCredentials>(jsonString, serializationOptions);

            if (config?.Auths != null && config.Auths.TryGetValue(registryLoginUrl, out var authEntry))
            {
                var decodedAuthBytes = Convert.FromBase64String(authEntry.Auth);
                
                var decodedAuthString = Encoding.UTF8.GetString(decodedAuthBytes);
                
                var splitIndex = decodedAuthString.IndexOf(':');
                
                if (splitIndex > 0)
                {
                    return new AuthConfig
                    {
                        ServerAddress = registryServerAddress,
                        Username = decodedAuthString.Substring(0, splitIndex),
                        Password = decodedAuthString.Substring(splitIndex + 1)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occured while trying to read docker config.json file");
        }

        return new AuthConfig();
    }
    
    private string TryGetLoginUrlForRegistry(string registryDomain)
    {
        var knownAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "docker.io", "https://index.docker.io/v1/" }, 
        };

        if (knownAliases.TryGetValue(registryDomain, out var specialAuthKey))
        {
            return specialAuthKey;
        }

        return registryDomain; 
    }
}
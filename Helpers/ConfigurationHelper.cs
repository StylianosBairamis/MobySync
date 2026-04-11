using System.Text.Json;
using docker_image_updater.Data.Models;

namespace docker_image_updater.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly string _updateConfigurationPath;
    
    private readonly UpdaterConfiguration _updaterConfiguration;
    
    private readonly ILogger<ConfigurationService> _logger;
    
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        _updateConfigurationPath = Path.Combine(baseDirectory, "updater-configuration.json");

        if (!File.Exists(_updateConfigurationPath))
        {
            throw new FileNotFoundException("Update configuration file could not be found", _updateConfigurationPath);
        }

        try
        {
            var jsonString = File.ReadAllText(_updateConfigurationPath);
            
            var serializationOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            _updaterConfiguration = JsonSerializer.Deserialize<UpdaterConfiguration>(jsonString, serializationOptions) 
                            ?? throw new JsonException("Failed to deserialize update configuration file");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "An unexpected error occurred while trying to load update configuration");
            
            throw;
        }
    }

    public UpdaterConfiguration GetConfiguration() => _updaterConfiguration;

    public async Task UpdateConfiguration(UpdaterConfiguration updaterConfiguration)
    {
        await _fileLock.WaitAsync();
        
        try
        { 
            _updaterConfiguration.Services = updaterConfiguration.Services;
            
            _updaterConfiguration.GenericSettings = updaterConfiguration.GenericSettings;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var jsonString = JsonSerializer.Serialize(_updaterConfiguration, options);
            
            await File.WriteAllTextAsync(_updateConfigurationPath, jsonString);
            
            _logger.LogInformation("Successfully update of configuration file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public bool Validate(UpdaterConfiguration config, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (config.GenericSettings.Hour < 0 || config.GenericSettings.Hour > 23)
        {
            errorMessage = "Provided update hour must be between 0 and 23";
            
            return false;
        }

        if (config.GenericSettings.Minute < 0 || config.GenericSettings.Minute > 59)
        {
            errorMessage = "Provided update minute must be between 0 and 59";
            
            return false;
        }

        foreach (var service in config.Services)
        {
            if (string.IsNullOrWhiteSpace(service.ImageName))
            {
                errorMessage = $"Service '{service.ImageName}' ImageName cannot be empty";
                
                return false;
            }
            if (string.IsNullOrWhiteSpace(service.TargetTag))
            {
                errorMessage = $"Service '{service.ImageName}' TargetTag cannot be empty";
                
                return false;
            }
        }
        return true;
    }
}

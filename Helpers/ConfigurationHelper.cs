using System.Text.Json;
using MobySync.Models;

namespace MobySync.Helpers;

public class ConfigurationHelper
{
    private readonly string _updateConfigurationPath;
    
    private readonly UpdaterConfiguration _updaterConfiguration;
    
    private readonly ILogger<ConfigurationHelper> _logger;
    
    private readonly SemaphoreSlim _updateConfigurationFileLock = new(1, 1);

    private readonly SemaphoreSlim _configChangedSignal = new(0, 1);

    public SemaphoreSlim ConfigChangedSignal => _configChangedSignal;

    public ConfigurationHelper(ILogger<ConfigurationHelper> logger)
    {
        _logger = logger;
        
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        _updateConfigurationPath = Path.Combine(baseDirectory, "Configuration", "updater-configuration.json");

        if (!File.Exists(_updateConfigurationPath))
        {
            throw new FileNotFoundException("updater-configuration.json file could not be found, exiting...", _updateConfigurationPath);
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
            _logger.LogCritical(ex, "An unexpected error occurred while trying to load update configuration, exiting...");
            
            throw;
        }
    }

    public UpdaterConfiguration GetConfiguration() => _updaterConfiguration;

    public async Task UpdateConfiguration(UpdaterConfiguration updaterConfiguration)
    {
        await _updateConfigurationFileLock.WaitAsync();
        
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

            if (_configChangedSignal.CurrentCount == 0)
            {
                _configChangedSignal.Release();
            }
        }
        finally
        {
            _updateConfigurationFileLock.Release();
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

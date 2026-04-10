using docker_image_updater.Data.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace docker_image_updater.Helpers;

public class DockerHelper
{
    private readonly DockerClient _dockerClient;
    
    private readonly UpdaterConfiguration _updaterConfiguration;
    
    private readonly ILogger<DockerHelper> _logger;
    
    public DockerHelper(UpdaterConfiguration updaterConfiguration, ILogger<DockerHelper> logger)
    {
        var dockerSocketUri = new Uri("unix:///var/run/docker.sock");

        _dockerClient = new DockerClientConfiguration(dockerSocketUri).CreateClient();
        
        _updaterConfiguration = updaterConfiguration;
        
        _logger = logger;   
    }
    
    public async Task CheckForImageUpdates()
    {
        var monitoredContainers = await GetMonitoredContainers();
        
        if (monitoredContainers == null || !monitoredContainers.Any())
            return;

        _logger.LogInformation("Update Execution Order: {Order}", 
            string.Join(" -> ", monitoredContainers.Select(c => c.Names.First().Replace("/", ""))));

        var successfulOperation = await PullImages(monitoredContainers);

        if (!successfulOperation)
            return;
        
        await ReplaceContainers(monitoredContainers);
        
        if (_updaterConfiguration.GenericSettings.PruneOldImages)
        {
            await _dockerClient.Images.PruneImagesAsync(new ImagesPruneParameters());
        }
    }
    
    private async Task<List<ContainerListResponse>?> GetMonitoredContainers()
    {
        var allContainers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        
        var enabledServices = _updaterConfiguration.Services.Where(service => service.Enabled)
                                                        .Select(service => $"{service.ImageName}")
                                                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        if (!enabledServices.Any())
        {
            _logger.LogInformation("No enabled services were found in configuration.");
            
            return null;
        }

        // Filter running containers that match enabled services.
        var monitoredContainers = allContainers.Where(container => container.State == "running")
            .Where(container => 
            {
                var imageName = FetchBaseImageName(container.Image); 

                return enabledServices.Contains(imageName);
            })
            .ToList();

        if (!monitoredContainers.Any())
        {
            _logger.LogInformation("No desired containers were found in running state.");
            
            return null;
        }
        
        return SortByDependencies(monitoredContainers);
    }

    private async Task<bool> PullImages(IEnumerable<ContainerListResponse> monitoredContainers)
    {
        foreach (var container in monitoredContainers)
        {
            var serviceConfig = _updaterConfiguration.Services
                .FirstOrDefault(service => container.Image.StartsWith(service.ImageName));

            if (serviceConfig == null)
            {
                return false;
            }
            
            try 
            {
                await _dockerClient.Images.CreateImageAsync(new ImagesCreateParameters
                {
                    FromImage = serviceConfig.ImageName,
                    Tag = serviceConfig.TargetTag
                }, new AuthConfig(), new Progress<JSONMessage>());

                _logger.LogInformation("Successfully pulled new image: {Image}", serviceConfig.ImageName);
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while trying to pull {Image}.\nException message:{ex}", 
                    serviceConfig.ImageName, ex.Message);

                return false;
            }
        }
        
        return true;
    }
    
    private async Task ReplaceContainers(IEnumerable<ContainerListResponse> monitoredContainers)
    {
        foreach (var container in monitoredContainers)
        {
            try
            {
                var containerConfiguration = await _dockerClient.Containers.InspectContainerAsync(container.ID);
                
                var baseImageName = FetchBaseImageName(container.Image);
                
                var serviceConfig = _updaterConfiguration.Services
                    .First(service => service.ImageName.Equals(baseImageName, StringComparison.OrdinalIgnoreCase));

                var createParams = new CreateContainerParameters(containerConfiguration.Config)
                {
                    Image = $"{serviceConfig.ImageName}:{serviceConfig.TargetTag}",
                    HostConfig = containerConfiguration.HostConfig,
                    NetworkingConfig = new NetworkingConfig { EndpointsConfig = containerConfiguration.NetworkSettings.Networks },
                    Name = container.Names.First().Replace("/", "")
                };

                await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });

                var response = await _dockerClient.Containers.CreateContainerAsync(createParams);
                
                await _dockerClient.Containers.StartContainerAsync(response.ID, null);
                
                _logger.LogInformation("Successfully updated {Container} to {Tag}", createParams.Name, serviceConfig.TargetTag);
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed to recreate container {Id}: {Msg}", container.ID, ex.Message);
            }
        }
    }
    
    private List<ContainerListResponse> SortByDependencies(IList<ContainerListResponse> containers)
    {
        var sortedList = new List<ContainerListResponse>();
        
        // Set used for containers that have been processed
        var visited = new HashSet<string>();
        
        // Set used to detect circular dependencies
        var visiting = new HashSet<string>();

        var containerMap = containers.ToDictionary(
            container =>  container.Names.First().Replace("/", ""),
            container => container
        );

        foreach (var container in containers)
        {
            Visit(container);
        }

        return sortedList;

        void Visit(ContainerListResponse container)
        {
            var containerName =  container.Names.First().Replace("/", "");
            
            // Dependency has already been resolved
            if (visited.Contains(containerName)) 
                return; 
            
            // Circular dependency detection
            if (visiting.Contains(containerName)) 
                throw new InvalidOperationException($"Critical: Circular dependency detected involving container '{containerName}'!");

            visiting.Add(containerName);

            // Traverse to the dependencies of the container.
            if (container.Labels.TryGetValue("com.update.depends-on", out var dependenciesString))
            {
                var dependencies = dependenciesString.Split(',')
                    .Select(dependency => dependency.Trim());

                foreach (var dependency in dependencies)
                {
                    if (containerMap.TryGetValue(dependency, out var dependencyContainer))
                    {
                        Visit(dependencyContainer); 
                    }
                }
            }

            // 2. Mark as visited and add to list AFTER dependencies are resolved
            visiting.Remove(containerName);
            
            visited.Add(containerName);
            
            sortedList.Add(container); 
        }
    }
    
    private string FetchBaseImageName(string imageFullPath)
    {
        var lastColonIndex = imageFullPath.LastIndexOf(':');
        
        var lastSlashIndex = imageFullPath.LastIndexOf('/');
        
        if (lastColonIndex > lastSlashIndex)
        {
            return imageFullPath.Substring(0, lastColonIndex);
        }

        return imageFullPath;
    }
    
    

   
}
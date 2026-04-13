using MobySync.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace MobySync.Helpers;

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
        try
        {
            var monitoredContainers = await GetMonitoredContainers();
        
            if (monitoredContainers is null)
                return;
        
            // See if the sequence is correct here
            var containersGroups = GroupDependentContainers(monitoredContainers);

            foreach (var containerGroup in containersGroups)
            {
                var outdatedContainers = await PullGroupImages(containerGroup.Value);

                if (outdatedContainers is null|| !outdatedContainers.Any())
                    continue;
                
                _logger.LogInformation("Update Execution Order: {Order}", 
                    string.Join(" -> ", outdatedContainers.Select(container => container.Names.First().Replace("/", ""))));
        
                await ReplaceContainers(outdatedContainers);
            }

            if (_updaterConfiguration.GenericSettings.PruneOldImages)
            {
                await _dockerClient.Images.PruneImagesAsync(new ImagesPruneParameters());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while trying to update the monitored containers.");
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

        if (monitoredContainers.Any()) 
            return monitoredContainers;
        
        _logger.LogInformation("No desired containers were found in running state.");
            
        return null;
    }
    
    private Dictionary<string, List<ContainerListResponse>> GroupDependentContainers(IList<ContainerListResponse> monitoredContainers)
    {
        var containerMap = monitoredContainers.ToDictionary(
            container => container.Names.First().Replace("/", ""),
            container => container);
        
        var adjacencyList = new Dictionary<string, HashSet<string>>();
        
        foreach (var name in containerMap.Keys)
        {
            adjacencyList[name] = new HashSet<string>();
        }
        
        foreach (var container in monitoredContainers)
        {
            var containerName = container.Names.First().Replace("/", "");
            
            if (container.Labels != null && container.Labels.TryGetValue("com.update.depends-on", out var deps))
            {
                foreach (var dependency in deps.Split(',').Select(dependency => dependency.Trim()))
                {
                    if (containerMap.ContainsKey(dependency))
                    {
                        adjacencyList[containerName].Add(dependency);
                        
                        adjacencyList[dependency].Add(containerName);
                    }
                }
            }
        }

        var visited = new HashSet<string>();
        
        var groupedDictionary = new Dictionary<string, List<ContainerListResponse>>();
        
        var groupCounter = 1;

        foreach (var nodeName in containerMap.Keys)
        {
            if (!visited.Contains(nodeName))
            {
                var currentGroupNames = new List<string>();
            
                ExploreNode(nodeName, currentGroupNames);

                var containersGroup = currentGroupNames.Select(name => containerMap[name]).ToList();
                
                var sortedGroup = SortContainersByDependencies(containersGroup);
            
                groupedDictionary.Add($"Auto-Group-{groupCounter}", sortedGroup);
                
                groupCounter++;
            }
        }

        return groupedDictionary;
        
        void ExploreNode(string current, List<string> group)
        {
            visited.Add(current);
            
            group.Add(current);

            foreach (var neighbor in adjacencyList[current])
            {
                if (!visited.Contains(neighbor))
                {
                    ExploreNode(neighbor, group);
                }
            }
        }
    }
    
    private async Task<IList<ContainerListResponse>?> PullGroupImages(IList<ContainerListResponse> monitoredContainers)
    {
        var outdatedContainers = new List<ContainerListResponse>();
        
        foreach (var container in monitoredContainers)
        {
            var baseImageName = FetchBaseImageName(container.Image);
                
            var serviceConfig = _updaterConfiguration.Services
                .FirstOrDefault(service => service.ImageName.Equals(baseImageName, StringComparison.OrdinalIgnoreCase));

            if (serviceConfig is null)
            {
                return null;
            }
            
            try 
            {
                await _dockerClient.Images.CreateImageAsync(new ImagesCreateParameters
                {
                    FromImage = baseImageName,
                    Tag = serviceConfig.TargetTag
                }, new AuthConfig(), new Progress<JSONMessage>());
                
                var pulledImageInformation = await _dockerClient.Images.InspectImageAsync($"{baseImageName}:{serviceConfig.TargetTag}");

                var pulledImageId = pulledImageInformation.ID;
                
                var runningImageId = container.ImageID;

                if (runningImageId != pulledImageId)
                {
                    outdatedContainers.Add(container);
                    
                    _logger.LogInformation("Successfully pulled new version for image: {Image}. New image ID: {ImageId}", 
                        serviceConfig.ImageName, pulledImageId);
                }
                else
                {
                    _logger.LogInformation("Container {Container} is already running the latest version ", 
                        container.Names.First().Replace("/", ""));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while trying to pull {Image}. Aborting group update.", 
                    serviceConfig.ImageName);

                return null;
            }
        }
        
        return outdatedContainers;
    }
    
    private async Task ReplaceContainers(IList<ContainerListResponse> containersForReplace) 
    {
        // Rollbacks are executed in the opposite direction 
        var transactionLog = new Stack<ContainerCheckpoint>();
        
        foreach (var containerForUpdate in containersForReplace)
        {
            var containerConfiguration = await _dockerClient.Containers.InspectContainerAsync(containerForUpdate.ID);
            
            var baseImageName = FetchBaseImageName(containerForUpdate.Image);
            
            var serviceConfig = _updaterConfiguration.Services
                .First(service => service.ImageName.Equals(baseImageName, StringComparison.OrdinalIgnoreCase));
            
            var newContainerId = string.Empty;
            
            var originalName = containerForUpdate.Names.First().Replace("/", "");

            var backupName = $"{originalName}-backup";
            
            try
            {
                var createParams = new CreateContainerParameters(containerConfiguration.Config)
                {
                    Image = $"{serviceConfig.ImageName}:{serviceConfig.TargetTag}",
                    HostConfig = containerConfiguration.HostConfig,
                    NetworkingConfig = new NetworkingConfig { EndpointsConfig = containerConfiguration.NetworkSettings.Networks },
                    Name = containerForUpdate.Names.First().Replace("/", "")
                };
                
                // Rename the target container for update, in order to avoid conflicts
                await _dockerClient.Containers.RenameContainerAsync(containerForUpdate.ID, new ContainerRenameParameters { NewName = backupName}, 
                    CancellationToken.None);
                
                // Stop the target container for update
                await _dockerClient.Containers.StopContainerAsync(containerForUpdate.ID, new ContainerStopParameters());

                var createdContainer = await _dockerClient.Containers.CreateContainerAsync(createParams);
                
                // Start the updated container
                var successfulContainerStart = await _dockerClient.Containers.StartContainerAsync(createdContainer.ID, new ContainerStartParameters());
                
                if (!successfulContainerStart)
                    throw new InvalidOperationException($""); // add a comment here
                
                // Wait some time in order to check the state of the created container
                await Task.Delay(TimeSpan.FromSeconds(10));
            
                var createdContainerConfiguration = await _dockerClient.Containers.InspectContainerAsync(createdContainer.ID);
                
                newContainerId =  createdContainerConfiguration.ID;
                
                if (!createdContainerConfiguration.State.Running)
                {
                    throw new InvalidOperationException($"Container {createdContainerConfiguration.Name} crashed after starting, " +
                                                        $"exit Code: {createdContainerConfiguration.State.ExitCode}");
                }
                
                // Add entry for rollback in case of a failure
                transactionLog.Push(new ContainerCheckpoint
                {
                    OldContainerId = containerConfiguration.ID,
                    NewContainerId = newContainerId,
                    ContainerOriginalName = containerConfiguration.Name,
                    ContainerBackupName = backupName
                });
                
                _logger.LogInformation("Successfully updated {Container} to {Tag}", createParams.Name, serviceConfig.TargetTag);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred while trying to replace container with id: {ContainerId}. " +
                                        "Reverting changes of previously updated containers", containerForUpdate.ID);
                
                transactionLog.Push(new ContainerCheckpoint
                {
                    OldContainerId = containerConfiguration.ID,
                    NewContainerId = newContainerId,
                    ContainerOriginalName = containerConfiguration.Name,
                    ContainerBackupName = backupName
                });

                await AttemptRollback(transactionLog);
                
                // send notification

                return;
            }
        }
        
        foreach (var checkpoint in transactionLog)
        {
            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(checkpoint.OldContainerId, new ContainerRemoveParameters { Force = true });
            }
            catch (Exception ex)
            {
                var backupContainerName = checkpoint.ContainerOriginalName + "-backup";
                
                _logger.LogWarning(ex, "Failed to remove backup container {BackupName}, manual intervention is needed", backupContainerName);
                
                // send notification
            }
        }
    }
    
    private async Task AttemptRollback(Stack<ContainerCheckpoint> transactionLog)
    {
        foreach (var checkpoint in transactionLog)
        {
            _logger.LogInformation("Starting rollback process for container: {Container}", checkpoint.ContainerOriginalName);
            
            try
            {
                if (!string.IsNullOrEmpty(checkpoint.NewContainerId))
                {
                    _logger.LogInformation("Removing failed container instance");
                    
                    await _dockerClient.Containers.RemoveContainerAsync(checkpoint.NewContainerId, new ContainerRemoveParameters { Force = true });
                }

                var oldContainerInspect = await _dockerClient.Containers.InspectContainerAsync(checkpoint.OldContainerId);
                
                // We only rename it back if it currently has the backup name
                if (oldContainerInspect.Name.Replace("/", "") == checkpoint.ContainerBackupName)
                {
                    _logger.LogInformation("Restoring original name...");
                    
                    await _dockerClient.Containers.RenameContainerAsync(checkpoint.OldContainerId, new ContainerRenameParameters { NewName = checkpoint.ContainerOriginalName }, CancellationToken.None);
                }
                
                await _dockerClient.Containers.StartContainerAsync(checkpoint.OldContainerId, new ContainerStartParameters());
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred while tying to rollback container: {Container}. " +
                                        "Manual intervention is needed. Backup container is {BackupName}", checkpoint.ContainerOriginalName, checkpoint.ContainerBackupName);
            }
        }
    }
    
    private List<ContainerListResponse> SortContainersByDependencies(IList<ContainerListResponse> containers)
    {
        // This list is sorted so that base dependencies update first, and dependent containers update lastly
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
                throw new InvalidOperationException($"An circular dependency has detected involving containers '{containerName}'"); // see to add which container is the conflict

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
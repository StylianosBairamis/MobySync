using MobySync.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using MobySync.Interfaces;

namespace MobySync.Helpers;

public class DockerHelper
{
    private readonly DockerClient _dockerClient;
    
    private readonly ILogger<DockerHelper> _logger;
    
    private readonly CredentialsHelper _credentialsHelper;
    
    private bool _pruneImages;
    
    public DockerHelper(ILogger<DockerHelper> logger, CredentialsHelper credentialsHelper)
    {
        var dockerSocketUri = new Uri("unix:///var/run/docker.sock");

        _dockerClient = new DockerClientConfiguration(dockerSocketUri).CreateClient();
        
        _logger = logger;
        
        _credentialsHelper = credentialsHelper; 
        
        _pruneImages = false;

        if (bool.TryParse(Environment.GetEnvironmentVariable("PRUNE_IMAGES"), out var pruneImagesParsed))
        {
            _pruneImages = pruneImagesParsed;
        }
    }
    
    public async Task<UpdateSummary> CheckForImageUpdates()
    {
        var updateSummary = new UpdateSummary();
        
        var startTime = DateTime.Now;

        try
        {
            var monitoredContainers = await GetMonitoredContainers();
        
            if (monitoredContainers is null)
                return updateSummary;
        
            var containersGroups = GroupDependentContainers(monitoredContainers);

            foreach (var containerGroup in containersGroups)
            {
                var outdatedContainers = await PullGroupImages(containerGroup.Value, updateSummary);

                if (outdatedContainers is null|| !outdatedContainers.Any())
                    continue;
                
                _logger.LogInformation("Group update execution order: {Order}", 
                    string.Join(" -> ", outdatedContainers.Select(container => container.Names.First().Replace("/", ""))));
        
                await ReplaceContainers(outdatedContainers, updateSummary);
            }

            if (_pruneImages)
            {
                await _dockerClient.Images.PruneImagesAsync(new ImagesPruneParameters());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while trying to update the monitored containers.");
        }
        finally
        {
            updateSummary.TotalDuration = DateTime.Now - startTime;
            
            _logger.LogInformation("Update cycle finished in {Duration}. Successes: {Success}, Rollbacks: {Rollback}, Failed Pulls: {Failed}", 
                updateSummary.TotalDuration.ToString(@"hh\:mm\:ss"), updateSummary.Successes.Count, updateSummary.Rollbacks.Count, updateSummary.FailedPulls.Count);
        }

        return updateSummary;
    }
    
    private async Task<List<ContainerListResponse>?> GetMonitoredContainers()
    {
        var allContainers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        
        var monitoredContainers = allContainers
            .Where(container => container.State == "running")
            .Where(container => 
            {
                if (container.Labels is not null && container.Labels.TryGetValue("com.mobysync.enable", out var isEnabledStr))
                {
                    bool.TryParse(isEnabledStr, out var isEnabled) ;
                    
                    return isEnabled;
                }
            
                return false;
            })
            .ToList();

        if (monitoredContainers.Any())
        {
            _logger.LogInformation("Found {Count} containers configured for monitoring.", monitoredContainers.Count);
    
            var containerNames = string.Join(", ", monitoredContainers.Select(container => container.Names.First().Replace("/", "")));
            
            _logger.LogInformation("Monitored containers: {Names}", containerNames);
            
            return monitoredContainers;
        }
        
        _logger.LogWarning("No containers were found in running state and monitoring enabled");
            
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
            
            if (container.Labels != null && container.Labels.TryGetValue("com.mobysync.depends-on", out var deps))
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
    
    private async Task<IList<ContainerListResponse>?> PullGroupImages(IList<ContainerListResponse> monitoredContainers, UpdateSummary summary)
    {
        var outdatedContainers = new List<ContainerListResponse>();
        
        foreach (var container in monitoredContainers)
        {
            var baseImageName = FetchBaseImageName(container.Image);
            var containerName = container.Names.First().Replace("/", "");
            
            // Default tag in case of a misconfiguration by the user
            var targetTag = "latest";
                
            if(container.Labels is not null && container.Labels.TryGetValue("com.mobysync.target-tag", out var targetTagParsed))
            {
                targetTag = targetTagParsed;
            }

            try
            {
                var authCredentials = await _credentialsHelper.FetchCredentials(baseImageName);
                
                await _dockerClient.Images.CreateImageAsync(new ImagesCreateParameters
                {
                    FromImage = baseImageName,
                    Tag = targetTag
                }, authCredentials, new Progress<JSONMessage>());
                
                var pulledImageInformation = await _dockerClient.Images.InspectImageAsync($"{baseImageName}:{targetTag}");

                var pulledImageId = pulledImageInformation.ID;
                
                var runningImageId = container.ImageID;

                if (runningImageId != pulledImageId)
                {
                    outdatedContainers.Add(container);
                    
                    _logger.LogInformation("Successfully pulled new version for image: {Image}. New image ID: {ImageId}", 
                        baseImageName, pulledImageId);
                }
                else
                {
                    _logger.LogInformation("Container {Container} is already running the latest version ", containerName);
                    
                    summary.UpToDate.Add(containerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while trying to pull {Image}. Aborting group update.", 
                    baseImageName);

                summary.FailedPulls.Add(new ContainerUpdateResult
                {
                    ContainerName = containerName,
                    ImageName = baseImageName,
                    ErrorMessage = ex.Message,
                    Success = false
                });

                return null;
            }
        }
        
        return outdatedContainers;
    }
    
    private async Task ReplaceContainers(IList<ContainerListResponse> containersForReplace, UpdateSummary summary) 
    {
        // Rollbacks are executed in the opposite direction 
        var transactionLog = new Stack<ContainerCheckpoint>();
        
        foreach (var containerForUpdate in containersForReplace)
        {
            var containerConfiguration = await _dockerClient.Containers.InspectContainerAsync(containerForUpdate.ID);
            
            var baseImageName = FetchBaseImageName(containerForUpdate.Image);
            
            // Default tag in case of a misconfiguration by the user
            var targetTag = "latest";
                
            if(containerForUpdate.Labels is not null && containerForUpdate.Labels.TryGetValue("com.mobysync.target-tag", out var targetTagParsed))
            {
                targetTag = targetTagParsed;
            }
            
            var newContainerId = string.Empty;
            
            var originalName = containerForUpdate.Names.First().Replace("/", "");

            var backupName = $"{originalName}-backup";
            
            try
            {
                var createParams = new CreateContainerParameters(containerConfiguration.Config)
                {
                    Image = $"{baseImageName}:{targetTag}",
                    HostConfig = containerConfiguration.HostConfig,
                    NetworkingConfig = new NetworkingConfig { EndpointsConfig = containerConfiguration.NetworkSettings.Networks },
                    Name = originalName
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
                    throw new InvalidOperationException($"Failed to start container {originalName}");
                
                // Wait some time in order to check the state of the created container
                await Task.Delay(TimeSpan.FromSeconds(10));
            
                var createdContainerConfiguration = await _dockerClient.Containers.InspectContainerAsync(createdContainer.ID);
                
                newContainerId =  createdContainerConfiguration.ID;
                
                if (!createdContainerConfiguration.State.Running)
                {
                    throw new InvalidOperationException($"Container {originalName} crashed after starting, " +
                                                        $"exit Code: {createdContainerConfiguration.State.ExitCode}");
                }
                
                // Add entry for rollback in case of a failure
                transactionLog.Push(new ContainerCheckpoint
                {
                    OldId = containerConfiguration.ID,
                    NewId = newContainerId,
                    OriginalName = originalName,
                    BackupName = backupName
                });

                summary.Successes.Add(new ContainerUpdateResult
                {
                    ContainerName = originalName,
                    ImageName = baseImageName,
                    NewTag = targetTag,
                    Success = true
                });
                
                _logger.LogInformation("Successfully updated {Container} to {Tag}", originalName, targetTag);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred while trying to replace container {Container}. " +
                                        "Reverting changes of previously updated containers", originalName);
                
                summary.Rollbacks.Add(new ContainerUpdateResult
                {
                    ContainerName = originalName,
                    ImageName = baseImageName,
                    ErrorMessage = ex.Message,
                    Success = false
                });

                transactionLog.Push(new ContainerCheckpoint
                {
                    OldId = containerConfiguration.ID,
                    NewId = newContainerId,
                    OriginalName = originalName,
                    BackupName = backupName
                });

                await AttemptRollback(transactionLog);

                return;
            }
        }
        
        foreach (var checkpoint in transactionLog)
        {
            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(checkpoint.OldId, new ContainerRemoveParameters { Force = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove backup container {BackupName}, manual intervention is needed", checkpoint.BackupName);
            }
        }
    }
    
    private async Task AttemptRollback(Stack<ContainerCheckpoint> transactionLog)
    {
        foreach (var checkpoint in transactionLog)
        {
            _logger.LogInformation("Starting rollback process for container: {Container}", checkpoint.OriginalName);
            
            try
            {
                if (!string.IsNullOrEmpty(checkpoint.NewId))
                {
                    _logger.LogInformation("Removing failed container instance");
                    
                    await _dockerClient.Containers.RemoveContainerAsync(checkpoint.NewId, new ContainerRemoveParameters { Force = true });
                }

                var oldContainerInspect = await _dockerClient.Containers.InspectContainerAsync(checkpoint.OldId);
                
                // We only rename it back if it currently has the backup name
                if (oldContainerInspect.Name.Replace("/", "") == checkpoint.BackupName)
                {
                    _logger.LogInformation("Restoring original name...");
                    
                    await _dockerClient.Containers.RenameContainerAsync(checkpoint.OldId, new ContainerRenameParameters { NewName = checkpoint.OriginalName }, CancellationToken.None);
                }
                
                await _dockerClient.Containers.StartContainerAsync(checkpoint.OldId, new ContainerStartParameters());
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred while tying to rollback container: {Container}. " +
                                        "Manual intervention is needed. Backup container is {BackupName}", checkpoint.OriginalName, checkpoint.BackupName);
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
            if (container.Labels.TryGetValue("com.mobysync.depends-on", out var dependenciesString))
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
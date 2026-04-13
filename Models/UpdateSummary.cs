namespace MobySync.Models;

public class UpdateSummary
{
     public List<ContainerUpdateResult> Successes { get; } = [];
     public List<ContainerUpdateResult> Rollbacks { get; } = [];
     public List<ContainerUpdateResult> FailedPulls { get; } = [];
     public List<string> UpToDate { get; } = [];
     public TimeSpan TotalDuration { get; set; } 
     public bool HasChanges => Successes.Any() || Rollbacks.Any() || FailedPulls.Any();
}
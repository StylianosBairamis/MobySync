namespace MobySync.Interfaces;

public interface INotificationHelper
{
    bool IsConfigured { get; } 
    string ProviderName { get; }
    string WebhookUrl { get; }
    Task SendSuccessUpdate(string containerName, string newTag);
    Task SendRollbackAlert(string containerName, string errorMessage);
    Task SendSummary(MobySync.Models.UpdateSummary summary);
}
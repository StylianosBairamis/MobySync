using MobySync.Models;

namespace MobySync.Interfaces;

public interface INotificationHelper
{
    bool IsConfigured { get; } 
    string ProviderName { get; }
    string WebhookUrl { get; }
    Task SendSummary(UpdateSummary summary);
    Task SendStartupTest();
    Task SendUpdateStarted();
}
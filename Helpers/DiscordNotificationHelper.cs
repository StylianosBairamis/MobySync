using MobySync.Interfaces;

namespace MobySync.Helpers;

public class DiscordNotificationHelper : INotificationHelper
{
    private const int SuccessColor = 3066993; // Green
    
    private const int ErrorColor = 15158332;  // Red
    
    public bool IsConfigured { get; }
    public string ProviderName { get; }
    public string WebhookUrl { get; } = string.Empty;
    
    private readonly ILogger<DiscordNotificationHelper> _logger;
    
    private IHttpClientFactory _httpClientFactory;

    public DiscordNotificationHelper(ILogger<DiscordNotificationHelper> logger, IHttpClientFactory httpClientFactory)
    {
        var providedUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL") ?? string.Empty;
        
        _logger =  logger;

        ProviderName = "Discord";
        
        _httpClientFactory = httpClientFactory;

        if (!string.IsNullOrWhiteSpace(providedUrl))
        {
            IsConfigured = true;
            
            WebhookUrl =  providedUrl;
        }
        else
        {
            _logger.LogWarning("Discord webhook url was not provided");
        }
    }

    public async Task SendSuccessUpdate(string containerName, string newTag)
    {
        var payload = new
        {
            username = "MobySync",
            avatar_url = "https://www.docker.com/wp-content/uploads/2022/03/Moby-logo.png", 
            embeds = new[]
            {
                new
                {
                    title = "🚀 Update Successful",
                    description = $"Successfully updated **{containerName}**.",
                    color = SuccessColor,
                    fields = new[]
                    {
                        new { name = "New Version", value = $"`{newTag}`", inline = true },
                        new { name = "Time", value = DateTime.Now.ToString("g"), inline = true }
                    }
                }
            }
        };

        await PostToDiscordAsync(payload);
    }

    public async Task SendRollbackAlert(string containerName, string errorMessage)
    {
        var payload = new
        {
            username = "MobySync",
            embeds = new[]
            {
                new
                {
                    title = "⚠️ Update Failed & Rolled Back",
                    description = $"An error occurred while updating **{containerName}**. The container has been safely rolled back to its previous state.",
                    color = ErrorColor,
                    fields = new[]
                    {
                        new { name = "Error Details", value = $"```{errorMessage}```", inline = false }
                    }
                }
            }
        };

        await PostToDiscordAsync(payload);
    }

    public async Task SendSummary(Models.UpdateSummary summary)
    {
        if (!summary.HasChanges) return;

        var fields = new List<object>();

        if (summary.Successes.Any())
        {
            fields.Add(new
            {
                name = "✅ Successful Updates",
                value = string.Join("\n", summary.Successes.Select(s => $"**{s.ContainerName}**: `{s.NewTag}`")),
                inline = false
            });
        }

        if (summary.Rollbacks.Any())
        {
            fields.Add(new
            {
                name = "⚠️ Rollbacks",
                value = string.Join("\n", summary.Rollbacks.Select(r => $"**{r.ContainerName}**: {r.ErrorMessage}")),
                inline = false
            });
        }

        if (summary.FailedPulls.Any())
        {
            fields.Add(new
            {
                name = "❌ Failed Pulls",
                value = string.Join("\n", summary.FailedPulls.Select(f => $"**{f.ContainerName}**: {f.ErrorMessage}")),
                inline = false
            });
        }

        var payload = new
        {
            username = "MobySync",
            avatar_url = "https://www.docker.com/wp-content/uploads/2022/03/Moby-logo.png",
            embeds = new[]
            {
                new
                {
                    title = "📊 Update Cycle Summary",
                    color = summary.Rollbacks.Any() || summary.FailedPulls.Any() ? ErrorColor : SuccessColor,
                    fields = fields.ToArray(),
                    footer = new { text = $"Total duration: {summary.TotalDuration:mm\\:ss}" },
                    timestamp = DateTime.Now
                }
            }
        };

        await PostToDiscordAsync(payload);
    }

    private async Task PostToDiscordAsync(object payload)
    {
        if (!IsConfigured) 
            return;

        var httpClient = _httpClientFactory.CreateClient();
        
        try
        {
            var response = await httpClient.PostAsJsonAsync(WebhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send Discord notification. Status Code: {StatusCode}. Response: {Body}", 
                    response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A network error occurred while attempting to reach Discord.");
        }
    }
}
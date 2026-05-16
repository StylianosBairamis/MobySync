using MobySync.Interfaces;

namespace MobySync.Helpers;

public class DiscordNotificationHelper : INotificationHelper
{
    private readonly int _successColor = 3066993; 
    
    private readonly int _errorColor = 15158332; 
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

    public async Task SendSummary(Models.UpdateSummary summary)
    {
        if (!summary.HasChanges) 
            return;

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
            embeds = new[]
            {
                new
                {
                    title = "Update Cycle Summary",
                    color = summary.Rollbacks.Any() || summary.FailedPulls.Any() ? _errorColor : _successColor,
                    fields = fields.ToArray(),
                    footer = new { text = $"Total duration: {summary.TotalDuration:mm\\:ss}" },
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }
            }
        };

        await PostToDiscordAsync(payload);
    }

    public async Task SendStartupTest()
    {
        var payload = new
        {
            username = "MobySync",
            embeds = new[]
            {
                new
                {
                    title = "MobySync Started",
                    description = "Webhook is configured and working.",
                    color = _successColor,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
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
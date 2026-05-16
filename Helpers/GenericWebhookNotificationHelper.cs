using MobySync.Interfaces;

namespace MobySync.Helpers;

public class GenericWebhookNotificationHelper : INotificationHelper
{
    public bool IsConfigured { get; }
    public string ProviderName { get; }
    public string WebhookUrl { get; } = string.Empty;

    private readonly ILogger<GenericWebhookNotificationHelper> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public GenericWebhookNotificationHelper(ILogger<GenericWebhookNotificationHelper> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        ProviderName = "Generic";

        var providedUrl = Environment.GetEnvironmentVariable("WEBHOOK_URL") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(providedUrl))
        {
            IsConfigured = true;
            WebhookUrl = providedUrl;
        }
        else
        {
            _logger.LogWarning("Generic webhook URL was not provided");
        }
    }

    public async Task SendStartupTest()
    {
        if (!IsConfigured)
            return;

        var payload = new
        {
            @event = "startup",
            timestamp = DateTime.UtcNow,
            message = "MobySync started. Webhook is configured and working."
        };

        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            var response = await httpClient.PostAsJsonAsync(WebhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Generic webhook returned non-success status {StatusCode}. Response: {Body}",
                    response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A network error occurred while attempting to reach the generic webhook");
        }
    }

    public async Task SendSummary(Models.UpdateSummary summary)
    {
        if (!IsConfigured || !summary.HasChanges)
            return;

        var payload = new
        {
            @event = "update_cycle",
            timestamp = DateTime.UtcNow,
            durationSeconds = (long)summary.TotalDuration.TotalSeconds,
            successes = summary.Successes.Select(s => new
            {
                container = s.ContainerName,
                image = s.ImageName,
                oldTag = s.OldTag,
                newTag = s.NewTag
            }),
            rollbacks = summary.Rollbacks.Select(r => new
            {
                container = r.ContainerName,
                image = r.ImageName,
                error = r.ErrorMessage
            }),
            failedPulls = summary.FailedPulls.Select(f => new
            {
                container = f.ContainerName,
                image = f.ImageName,
                error = f.ErrorMessage
            }),
            upToDate = summary.UpToDate
        };

        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            var response = await httpClient.PostAsJsonAsync(WebhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Generic webhook returned non-success status {StatusCode}. Response: {Body}",
                    response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A network error occurred while attempting to reach the generic webhook");
        }
    }
}

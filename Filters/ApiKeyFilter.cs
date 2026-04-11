namespace docker_image_updater.Filters;

public class ApiKeyFilter : IEndpointFilter
{
    private readonly string _expectedApiKey;

    public ApiKeyFilter()
    {
        _expectedApiKey = Environment.GetEnvironmentVariable("API_KEY");
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
        {
            return Results.Unauthorized();
        }

        if (!_expectedApiKey.Equals(extractedApiKey))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}

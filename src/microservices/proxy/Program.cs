var monolithUrl = Environment.GetEnvironmentVariable("MONOLITH_URL") ?? "http://localhost:8080";
var moviesServiceUrl = Environment.GetEnvironmentVariable("MOVIES_SERVICE_URL") ?? "http://localhost:8081";
var eventsServiceUrl = Environment.GetEnvironmentVariable("EVENTS_SERVICE_URL") ?? "http://localhost:8082";
var gradualMigration = string.Equals(
    Environment.GetEnvironmentVariable("GRADUAL_MIGRATION"), "true",
    StringComparison.OrdinalIgnoreCase);
var moviesMigrationPercent = int.TryParse(
    Environment.GetEnvironmentVariable("MOVIES_MIGRATION_PERCENT"), out var pct) ? pct : 0;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8000");
    options.ListenAnyIP(port);
});

builder.Services.AddHttpClient("proxy", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
});

var app = builder.Build();

// Hop-by-hop headers must NOT be forwarded
var hopByHopHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
    "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Host"
};

// Health check – own endpoint, not forwarded
app.MapGet("/health", () => Results.Ok("Strangler Fig Proxy is healthy"));

// Catch-all reverse proxy
app.Map("/{**catchAll}", async (HttpContext context, IHttpClientFactory factory) =>
{
    var path = context.Request.Path.Value ?? "/";
    var query = context.Request.QueryString.Value ?? "";

    // --- Strangler Fig routing ---
    string targetBase;
    if (path.StartsWith("/api/events", StringComparison.OrdinalIgnoreCase))
    {
        targetBase = eventsServiceUrl;
    }
    else if (path.StartsWith("/api/movies", StringComparison.OrdinalIgnoreCase))
    {
        if (gradualMigration && moviesMigrationPercent > 0)
        {
            targetBase = Random.Shared.Next(0, 100) < moviesMigrationPercent
                ? moviesServiceUrl
                : monolithUrl;
        }
        else
        {
            targetBase = monolithUrl;
        }
    }
    else
    {
        targetBase = monolithUrl;
    }
    // ----------------------------

    var targetUrl = $"{targetBase}{path}{query}";

    var request = new HttpRequestMessage
    {
        Method = new HttpMethod(context.Request.Method),
        RequestUri = new Uri(targetUrl),
    };

    // Copy request headers (skip hop-by-hop and content headers)
    foreach (var (key, values) in context.Request.Headers)
    {
        if (!hopByHopHeaders.Contains(key) &&
            !key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(key, (IEnumerable<string>)values);
        }
    }

    // Attach body for methods that carry one
    if (context.Request.ContentLength > 0 ||
        context.Request.Headers.ContainsKey("Content-Type"))
    {
        var bodyContent = new StreamContent(context.Request.Body);
        foreach (var (key, values) in context.Request.Headers)
        {
            if (key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                bodyContent.Headers.TryAddWithoutValidation(key, (IEnumerable<string>)values);
        }
        request.Content = bodyContent;
    }

    var client = factory.CreateClient("proxy");
    try
    {
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;

        // Copy response headers
        foreach (var (key, values) in response.Headers)
        {
            if (!hopByHopHeaders.Contains(key))
                context.Response.Headers[key] = values.ToArray();
        }
        foreach (var (key, values) in response.Content.Headers)
        {
            if (!hopByHopHeaders.Contains(key))
                context.Response.Headers[key] = values.ToArray();
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (HttpRequestException ex)
    {
        context.Response.StatusCode = 502;
        await context.Response.WriteAsJsonAsync(new { error = "Bad Gateway", target = targetUrl, details = ex.Message });
    }
});

app.Run();

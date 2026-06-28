using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeCodeOllamaProxy.Bridge;
using ClaudeCodeOllamaProxy.Endpoints;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;
using ClaudeCodeOllamaProxy.Service.Logging;
using ClaudeCodeOllamaProxy.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeOllamaProxy;

/// <summary>
/// Builds the configured ClaudeCodeOllamaProxy <see cref="WebApplication"/> — URL binding, rolling file
/// logging, snake_case wire JSON, DI singletons, the wire-trace middleware, and the three endpoint
/// groups. Shared by the standalone console host and the WinUI tray app (which hosts it in-process).
/// </summary>
public static class ProxyHost
{
    /// <summary>
    /// Creates and configures the proxy host. <paramref name="configure"/> runs just before
    /// <c>builder.Build()</c>, letting an in-process host (e.g. the tray app) inject extra services or
    /// logging providers.
    /// </summary>
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        // Root content (appsettings.json, Logs/) at the output directory rather than the current working
        // directory. appsettings.json ships as copied content of this library, so it lands in the host's
        // bin folder — this lets both the console host and the in-process WinUI host find it regardless
        // of the process working directory.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Listen on the default Ollama port so VS Copilot Chat's BYOK Ollama provider needs zero config.
        // Loopback only: this proxy launches the local claude CLI, so it must not be exposed externally.
        var urlsConfigured = !string.IsNullOrEmpty(builder.Configuration["urls"])
            || !string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"])
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
        if (!urlsConfigured)
            builder.WebHost.UseUrls("http://127.0.0.1:11434");

        // Rolling file logs (unique per launch via <launch>; rolls daily and at 10 MB via <date>/<counter>).
        var logsDir = Path.Combine(builder.Environment.ContentRootPath, "Logs");
        LaunchPlaceholderResolver.SweepOldLogs(logsDir, keep: 3);
        builder.Logging.AddFile(o =>
        {
            o.RootPath = builder.Environment.ContentRootPath;
            o.PathPlaceholderResolver = LaunchPlaceholderResolver.Resolve;
        });

        // Console formatter that pretty-prints JSON in any log message (selected via Logging:Console:FormatterName).
        builder.Logging.AddConsoleFormatter<PrettyJsonConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();

        // Wire body binding + WriteAsJsonAsync to the snake_case Ollama/OpenAI wire format.
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        });

        builder.Services.Configure<ProxyOptions>(builder.Configuration);

        builder.Services.AddSingleton<ModelCatalog>();
        builder.Services.AddSingleton<BridgeRegistry>();
        builder.Services.AddSingleton<ModeDetector>();
        builder.Services.AddSingleton<WorkingDirectoryResolver>();
        builder.Services.AddSingleton<ClaudeSessionFactory>();
        builder.Services.AddSingleton<ImageMaterializer>();
        builder.Services.AddSingleton<ChatService>();

        configure?.Invoke(builder);

        var app = builder.Build();

        app.UseWireTraceLogging();

        app.MapOllamaDiscoveryEndpoints();
        app.MapOllamaChatEndpoint();
        app.MapOpenAiChatEndpoint();

        // Print the bound URL to the console once the server is listening.
        var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ClaudeCodeOllamaProxy.Startup");
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
            var urls = addresses is { Count: > 0 } ? string.Join(", ", addresses) : "http://127.0.0.1:11434";
            startupLogger.LogInformation("ClaudeCodeOllamaProxy listening on {Urls} — set this as the Ollama endpoint in Copilot Chat.", urls);
        });

        // Warm the model catalog once the server is up (queries the claude CLI once; falls back gracefully
        // if unavailable). Runs from the ApplicationStarted hook so both hosts benefit without awaiting.
        app.Lifetime.ApplicationStarted.Register(() => _ = WarmCatalogAsync(app, startupLogger));

        return app;
    }

    private static async Task WarmCatalogAsync(WebApplication app, ILogger logger)
    {
        try
        {
            var catalog = app.Services.GetRequiredService<ModelCatalog>();
            var models = await catalog.GetModelsAsync();
            logger.LogInformation("ClaudeCodeOllamaProxy ready. Models: {Models}",
                string.Join(", ", models.Select(m => m.Id)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to warm the model catalog at startup.");
        }
    }
}

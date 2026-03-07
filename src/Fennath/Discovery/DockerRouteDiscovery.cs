using Docker.DotNet;
using Docker.DotNet.Models;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Discovery;

/// <summary>
/// Discovers routes from Docker container labels and subscribes to container
/// start/stop events. Containers opt in with <c>fennath.subdomain</c> and
/// <c>fennath.backend</c> labels.
///
/// <para>Supported labels:</para>
/// <list type="bullet">
///   <item><c>fennath.subdomain</c> — required; comma-separated list of subdomains
///     (e.g. "www", "@,www", "grafana"). Use "@" for the apex/root domain.</item>
///   <item><c>fennath.backend</c> — required; the backend URL
///     (e.g. "http://localhost:3000" or "http://172.17.0.2:8080").</item>
///   <item><c>fennath.healthcheck.path</c> — optional; health check path.</item>
///   <item><c>fennath.healthcheck.interval</c> — optional; health check interval in seconds.</item>
/// </list>
/// </summary>
public sealed partial class DockerRouteDiscovery : IRouteDiscovery, IAsyncDisposable
{
    private const string LabelPrefix = "fennath.";
    private const string SubdomainLabel = "fennath.subdomain";
    private const string BackendLabel = "fennath.backend";
    private const string HealthCheckPathLabel = "fennath.healthcheck.path";
    private const string HealthCheckIntervalLabel = "fennath.healthcheck.interval";

    private readonly DockerClient _client;
    private readonly ILogger<DockerRouteDiscovery> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private List<DiscoveredRoute> _routes = [];
    private Task? _eventListenerTask;

    public event Action? RoutesChanged;

    public DockerRouteDiscovery(
        IOptions<FennathConfig> options,
        ILogger<DockerRouteDiscovery> logger)
    {
        var config = options.Value;
        _client = new DockerClientConfiguration(
            new Uri(config.Docker.SocketPath.StartsWith('/')
                ? $"unix://{config.Docker.SocketPath}"
                : config.Docker.SocketPath))
            .CreateClient();
        _logger = logger;
    }

    /// <summary>
    /// Queries current containers and starts listening for events.
    /// Called after DI construction to allow async initialization.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        await RefreshFromRunningContainersAsync(ct);
        _eventListenerTask = ListenForEventsAsync(_cts.Token);
        LogStarted(_logger, _routes.Count);
    }

    public IReadOnlyList<DiscoveredRoute> GetRoutes() => _routes;

    private async Task RefreshFromRunningContainersAsync(CancellationToken ct)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [SubdomainLabel] = true }
                }
            }, ct);

        var routes = new List<DiscoveredRoute>();
        foreach (var container in containers)
        {
            routes.AddRange(ParseContainerRoutes(container.ID[..12], container.Labels));
        }

        lock (_lock)
        {
            _routes = routes;
        }
    }

    private async Task ListenForEventsAsync(CancellationToken ct)
    {
        try
        {
            var progress = new Progress<Message>(OnDockerEvent);
            await _client.System.MonitorEventsAsync(
                new ContainerEventsParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["type"] = new Dictionary<string, bool> { ["container"] = true },
                        ["event"] = new Dictionary<string, bool>
                        {
                            ["start"] = true,
                            ["die"] = true
                        }
                    }
                },
                progress,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogEventListenerFailed(_logger, ex);
        }
    }

    private void OnDockerEvent(Message message)
    {
        var containerId = message.ID?[..Math.Min(12, message.ID.Length)] ?? "unknown";

        switch (message.Action)
        {
            case "start":
                LogContainerStarted(_logger, containerId);
                HandleContainerStart(containerId, message.Actor?.Attributes);
                break;
            case "die":
                LogContainerStopped(_logger, containerId);
                HandleContainerStop(containerId);
                break;
        }
    }

    private void HandleContainerStart(string containerId, IDictionary<string, string>? labels)
    {
        if (labels is null) return;

        var newRoutes = ParseContainerRoutes(containerId, labels);
        if (newRoutes.Count == 0)
        {
            LogContainerIgnored(_logger, containerId);
            return;
        }

        lock (_lock)
        {
            var updated = new List<DiscoveredRoute>(_routes);
            updated.AddRange(newRoutes);
            _routes = updated;
        }

        foreach (var route in newRoutes)
        {
            LogRouteAdded(_logger, route.Subdomain, route.BackendUrl, containerId);
        }

        RoutesChanged?.Invoke();
    }

    private void HandleContainerStop(string containerId)
    {
        var source = $"docker:{containerId}";
        List<DiscoveredRoute> removed;

        lock (_lock)
        {
            removed = _routes.Where(r => r.Source == source).ToList();
            if (removed.Count == 0) return;
            _routes = _routes.Where(r => r.Source != source).ToList();
        }

        foreach (var route in removed)
        {
            LogRouteRemoved(_logger, route.Subdomain, containerId);
        }

        RoutesChanged?.Invoke();
    }

    internal static List<DiscoveredRoute> ParseContainerRoutes(
        string containerId, IDictionary<string, string> labels)
    {
        if (!labels.TryGetValue(SubdomainLabel, out var subdomainValue)
            || string.IsNullOrWhiteSpace(subdomainValue))
        {
            return [];
        }

        if (!labels.TryGetValue(BackendLabel, out var backend)
            || string.IsNullOrWhiteSpace(backend))
        {
            return [];
        }

        labels.TryGetValue(HealthCheckPathLabel, out var healthPath);
        int? healthInterval = labels.TryGetValue(HealthCheckIntervalLabel, out var intervalStr)
            && int.TryParse(intervalStr, out var parsed)
            ? parsed
            : null;

        var source = $"docker:{containerId}";
        var subdomains = subdomainValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return subdomains
            .Select(sub => new DiscoveredRoute(
                Subdomain: sub,
                BackendUrl: backend,
                Source: source,
                HealthCheckPath: healthPath,
                HealthCheckIntervalSeconds: healthInterval))
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_eventListenerTask is not null)
        {
            try
            {
                await _eventListenerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts.Dispose();
        _client.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Docker route discovery started, found {count} routes from running containers")]
    private static partial void LogStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Container {containerId} started")]
    private static partial void LogContainerStarted(ILogger logger, string containerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Container {containerId} stopped")]
    private static partial void LogContainerStopped(ILogger logger, string containerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Container {containerId} has no fennath labels, ignoring")]
    private static partial void LogContainerIgnored(ILogger logger, string containerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route added: {subdomain} → {backend} (container {containerId})")]
    private static partial void LogRouteAdded(ILogger logger, string subdomain, string backend, string containerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route removed: {subdomain} (container {containerId})")]
    private static partial void LogRouteRemoved(ILogger logger, string subdomain, string containerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Docker event listener failed, dynamic route updates will stop")]
    private static partial void LogEventListenerFailed(ILogger logger, Exception ex);
}

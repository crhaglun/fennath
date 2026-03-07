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
    private const string SubdomainLabel = "fennath.subdomain";
    private const string BackendLabel = "fennath.backend";
    private const string HealthCheckPathLabel = "fennath.healthcheck.path";
    private const string HealthCheckIntervalLabel = "fennath.healthcheck.interval";

    private readonly DockerClient _client;
    private readonly DockerClient _eventClient;
    private readonly ILogger<DockerRouteDiscovery> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private List<DiscoveredRoute> _routes = [];
    private Task? _eventListenerTask;
    private Timer? _debounceTimer;
    private const int DebounceMs = 1500;

    public event Action? RoutesChanged;

    public DockerRouteDiscovery(
        IOptions<FennathConfig> options,
        ILogger<DockerRouteDiscovery> logger)
    {
        var config = options.Value;
        var uri = new Uri(config.Docker.SocketPath.StartsWith('/')
            ? $"unix://{config.Docker.SocketPath}"
            : config.Docker.SocketPath);
        _client = new DockerClientConfiguration(uri).CreateClient();
        // Separate client for the long-lived event stream so it doesn't
        // contend with request/response calls on the Unix socket.
        _eventClient = new DockerClientConfiguration(uri).CreateClient();
        _logger = logger;
    }

    /// <summary>
    /// Queries current containers and starts listening for events.
    /// Called after DI construction to allow async initialization.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await RefreshFromRunningContainersAsync(ct);
            _eventListenerTask = ListenForEventsAsync(_cts.Token);
            LogStarted(_logger, _routes.Count);
            foreach (var route in _routes)
            {
                LogRouteAdded(_logger, route.Subdomain, route.BackendUrl, route.Source);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStartFailed(_logger, ex);
        }
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _eventClient.System.MonitorEventsAsync(
                    new ContainerEventsParameters
                    {
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["type"] = new Dictionary<string, bool> { ["container"] = true }
                        }
                    },
                    new DirectProgress<Message>(OnDockerEvent),
                    ct);

                // MonitorEventsAsync returned — stream ended, reconnect
                LogEventStreamEnded(_logger);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogEventListenerFailed(_logger, ex);
                // Back off before reconnecting
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    /// <summary>
    /// IProgress that invokes the callback synchronously on the reporting thread,
    /// unlike <see cref="Progress{T}"/> which posts to the captured SynchronizationContext.
    /// </summary>
    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private void OnDockerEvent(Message message)
    {
        LogEventReceived(_logger, message.Action ?? "unknown");

        // Trailing-edge debounce: reset the timer on each event so we refresh
        // once after events stop arriving, rather than on the first event.
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => _ = DebouncedRefreshAsync(), null, DebounceMs, Timeout.Infinite);
    }

    private async Task DebouncedRefreshAsync()
    {
        LogRefreshTriggered(_logger);
        try
        {
            var previous = _routes;
            await RefreshFromRunningContainersAsync(CancellationToken.None);
            var current = _routes;

            LogDiff(previous, current);
            RoutesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogRefreshFailed(_logger, ex);
        }
    }

    private void LogDiff(IReadOnlyList<DiscoveredRoute> previous, IReadOnlyList<DiscoveredRoute> current)
    {
        var prevSet = previous.ToHashSet();
        var currSet = current.ToHashSet();

        foreach (var route in currSet.Except(prevSet))
        {
            LogRouteAdded(_logger, route.Subdomain, route.BackendUrl, route.Source);
        }

        foreach (var route in prevSet.Except(currSet))
        {
            LogRouteRemoved(_logger, route.Subdomain, route.Source);
        }
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
        _debounceTimer?.Dispose();

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
        _eventClient.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Docker route discovery started, found {count} routes from running containers")]
    private static partial void LogStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Docker event received: {action}")]
    private static partial void LogEventReceived(ILogger logger, string action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Debounced refresh triggered, re-reading containers")]
    private static partial void LogRefreshTriggered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route added: {subdomain} → {backend} ({source})")]
    private static partial void LogRouteAdded(ILogger logger, string subdomain, string backend, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route removed: {subdomain} ({source})")]
    private static partial void LogRouteRemoved(ILogger logger, string subdomain, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Docker discovery failed to start — container route discovery will be unavailable. Check that the Docker socket is mounted and accessible.")]
    private static partial void LogStartFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh container routes after Docker event")]
    private static partial void LogRefreshFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Docker event stream ended, reconnecting")]
    private static partial void LogEventStreamEnded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Docker event listener failed, reconnecting in 5s")]
    private static partial void LogEventListenerFailed(ILogger logger, Exception ex);
}

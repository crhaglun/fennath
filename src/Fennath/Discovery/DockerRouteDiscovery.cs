using Docker.DotNet;
using Docker.DotNet.Models;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Discovery;

/// <summary>
/// Discovers routes from Docker container labels by polling the Docker API.
/// Containers opt in with <c>fennath.subdomain</c> and <c>fennath.backend</c> labels.
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
    private readonly ILogger<DockerRouteDiscovery> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private List<DiscoveredRoute> _routes = [];
    private Task? _pollTask;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

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
        _logger = logger;
    }

    /// <summary>
    /// Queries current containers and starts the polling loop.
    /// Called after DI construction to allow async initialization.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await RefreshFromRunningContainersAsync(ct);
            _pollTask = PollForChangesAsync(_cts.Token);
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

    private async Task PollForChangesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);

                var previous = _routes;
                await RefreshFromRunningContainersAsync(ct);
                var current = _routes;

                if (HasChanges(previous, current))
                {
                    LogChanges(previous, current);
                    RoutesChanged?.Invoke();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPollFailed(_logger, ex);
            }
        }
    }

    private static bool HasChanges(IReadOnlyList<DiscoveredRoute> previous, IReadOnlyList<DiscoveredRoute> current) =>
        !previous.ToHashSet().SetEquals(current);

    private void LogChanges(IReadOnlyList<DiscoveredRoute> previous, IReadOnlyList<DiscoveredRoute> current)
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

        if (_pollTask is not null)
        {
            try { await _pollTask; }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
        _client.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Docker route discovery started, found {count} routes from running containers")]
    private static partial void LogStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route added: {subdomain} → {backend} ({source})")]
    private static partial void LogRouteAdded(ILogger logger, string subdomain, string backend, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route removed: {subdomain} ({source})")]
    private static partial void LogRouteRemoved(ILogger logger, string subdomain, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Docker discovery failed to start — container route discovery will be unavailable. Check that the Docker socket is mounted and accessible.")]
    private static partial void LogStartFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to poll Docker for container changes")]
    private static partial void LogPollFailed(ILogger logger, Exception ex);
}

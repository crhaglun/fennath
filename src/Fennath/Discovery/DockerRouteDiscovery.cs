using Docker.DotNet;
using Docker.DotNet.Models;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Discovery;

/// <summary>
/// Discovers routes from Docker container labels by polling the Docker API.
/// Containers opt in with <c>fennath.subdomain</c> label. The backend URL is
/// derived from the container name and optional <c>fennath.port</c> label.
///
/// <para>Supported labels:</para>
/// <list type="bullet">
///   <item><c>fennath.subdomain</c> — required; comma-separated list of subdomains
///     (e.g. "www", "@,www", "grafana"). Use "@" for the apex/root domain.</item>
///   <item><c>fennath.port</c> — optional; backend port (default 80).</item>
///   <item><c>fennath.healthcheck.path</c> — optional; health check path.</item>
///   <item><c>fennath.healthcheck.interval</c> — optional; health check interval in seconds.</item>
/// </list>
/// </summary>
public sealed partial class DockerRouteDiscovery(
    IOptions<FennathConfig> Options,
    ILogger<DockerRouteDiscovery> Logger) : BackgroundService, IRouteDiscovery
{
    private const string SubdomainLabel = "fennath.subdomain";
    private const string PortLabel = "fennath.port";
    private const string HealthCheckPathLabel = "fennath.healthcheck.path";
    private const string HealthCheckIntervalLabel = "fennath.healthcheck.interval";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly DockerClient Client = new DockerClientConfiguration(
        new Uri(Options.Value.Docker.SocketPath.StartsWith('/')
            ? $"unix://{Options.Value.Docker.SocketPath}"
            : Options.Value.Docker.SocketPath))
        .CreateClient();

    private readonly Lock _lock = new();
    private List<DiscoveredRoute> _routes = [];

    public event Action? RoutesChanged;

    public IReadOnlyList<DiscoveredRoute> GetRoutes() => _routes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshFromRunningContainersAsync(stoppingToken);
            LogStarted(Logger, _routes.Count);
            foreach (var route in _routes)
            {
                LogRouteAdded(Logger, route.Subdomain, route.BackendUrl, route.Source);
            }

            RoutesChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStartFailed(Logger, ex);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);

                var previous = _routes;
                await RefreshFromRunningContainersAsync(stoppingToken);
                var current = _routes;

                if (HasChanges(previous, current, out var added, out var removed))
                {
                    foreach (var route in added)
                        LogRouteAdded(Logger, route.Subdomain, route.BackendUrl, route.Source);
                    foreach (var route in removed)
                        LogRouteRemoved(Logger, route.Subdomain, route.Source);
                    RoutesChanged?.Invoke();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPollFailed(Logger, ex);
            }
        }
    }

    private async Task RefreshFromRunningContainersAsync(CancellationToken ct)
    {
        var containers = await Client.Containers.ListContainersAsync(
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
            var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];
            routes.AddRange(ParseContainerRoutes(container.ID[..12], name, container.Labels));
        }

        lock (_lock)
        {
            _routes = routes;
        }
    }

    private static bool HasChanges(
        IReadOnlyList<DiscoveredRoute> previous,
        IReadOnlyList<DiscoveredRoute> current,
        out IEnumerable<DiscoveredRoute> added,
        out IEnumerable<DiscoveredRoute> removed)
    {
        var prevSet = previous.ToHashSet();
        var currSet = current.ToHashSet();
        added = currSet.Except(prevSet);
        removed = prevSet.Except(currSet);
        return !prevSet.SetEquals(currSet);
    }

    internal static List<DiscoveredRoute> ParseContainerRoutes(
        string containerId, string containerName, IDictionary<string, string> labels)
    {
        if (!labels.TryGetValue(SubdomainLabel, out var subdomainValue)
            || string.IsNullOrWhiteSpace(subdomainValue))
        {
            return [];
        }

        var port = labels.TryGetValue(PortLabel, out var portStr)
            && int.TryParse(portStr, out var parsedPort)
            ? parsedPort
            : 80;
        var backend = $"http://{containerName}:{port}";

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

    public override void Dispose()
    {
        Client.Dispose();
        base.Dispose();
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Docker route discovery started, found {count} routes from running containers")]
    private static partial void LogStarted(ILogger logger, int count);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Route added: {subdomain} → {backend} ({source})")]
    private static partial void LogRouteAdded(ILogger logger, string subdomain, string backend, string source);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Route removed: {subdomain} ({source})")]
    private static partial void LogRouteRemoved(ILogger logger, string subdomain, string source);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Docker discovery failed to start — container route discovery will be unavailable. Check that the Docker socket is mounted and accessible.")]
    private static partial void LogStartFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "Failed to poll Docker for container changes")]
    private static partial void LogPollFailed(ILogger logger, Exception ex);
}

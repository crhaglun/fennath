using Docker.DotNet;
using Fennath.Dns;
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
/// </list>
/// </summary>
public sealed partial class DockerRouteDiscovery(
    IOptions<FennathConfig> Options,
    DnsReconciliationTrigger DnsTrigger,
    ILogger<DockerRouteDiscovery> Logger) : BackgroundService, IRouteDiscovery
{
    private const string SubdomainLabel = "fennath.subdomain";
    private const string PortLabel = "fennath.port";
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
        do
        {
            try
            {
                var previous = _routes;
                await RefreshFromRunningContainersAsync(stoppingToken);
                var current = _routes;

                if (LogAndDetectChanges(previous, current))
                {
                    RoutesChanged?.Invoke();
                    DnsTrigger.Signal("routes-changed");
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

            await Task.Delay(PollInterval, stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
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

        LogContainersFound(Logger, containers.Count);

        var routes = new List<DiscoveredRoute>();
        foreach (var container in containers)
        {
            var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];
            try
            {
                routes.AddRange(ParseContainerRoutes(container.ID[..12], name, container.Labels));
            }
            catch (Exception ex)
            {
                LogContainerParseFailed(Logger, name, ex);
            }
        }

        lock (_lock)
        {
            _routes = routes;
        }
    }

    private bool LogAndDetectChanges(
        IReadOnlyList<DiscoveredRoute> previous,
        IReadOnlyList<DiscoveredRoute> current)
    {
        var prevSet = previous.ToHashSet();
        var currSet = current.ToHashSet();

        if (prevSet.SetEquals(currSet))
        {
            return false;
        }

        foreach (var route in currSet.Except(prevSet))
        {
            LogRouteAdded(Logger, route.Subdomain, route.BackendUrl, route.Source);
        }

        foreach (var route in prevSet.Except(currSet))
        {
            LogRouteRemoved(Logger, route.Subdomain, route.Source);
        }

        return true;
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

        var source = $"docker:{containerId}";
        var subdomains = subdomainValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return subdomains
            .Select(sub => new DiscoveredRoute(
                Subdomain: sub,
                BackendUrl: backend,
                Source: source))
            .ToList();
    }

    public override void Dispose()
    {
        Client.Dispose();
        base.Dispose();
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Route added: {subdomain} → {backend} ({source})")]
    private static partial void LogRouteAdded(ILogger logger, string subdomain, string backend, string source);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Route removed: {subdomain} ({source})")]
    private static partial void LogRouteRemoved(ILogger logger, string subdomain, string source);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "Failed to poll Docker for container changes")]
    private static partial void LogPollFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "Docker poll found {count} labeled containers")]
    private static partial void LogContainersFound(ILogger logger, int count);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "Failed to parse routes for container {containerName}")]
    private static partial void LogContainerParseFailed(ILogger logger, string containerName, Exception ex);
}

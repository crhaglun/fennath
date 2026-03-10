using System.Text.Json;
using System.Text.Json.Nodes;
using Fennath.Configuration;
using Fennath.Core;
using Fennath.Discovery;
using Fennath.Operator.Dns;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Discovery;

/// <summary>
/// Watches route discovery sources and writes a YARP-format proxy configuration
/// JSON file to the shared volume. The proxy loads this file with
/// <c>AddJsonFile(reloadOnChange: true)</c> and YARP's <c>LoadFromConfig()</c>.
///
/// Also sends <see cref="DnsCommand.SubdomainAdded"/> for newly discovered
/// subdomains so the DNS reconciliation service can create A records.
/// </summary>
public sealed partial class ProxyConfigWriter(
    IEnumerable<IRouteDiscovery> sources,
    DnsCommandChannel dnsChannel,
    IOptions<FennathConfig> config,
    ILogger<ProxyConfigWriter> logger) : IHostedService, IDisposable
{
    private readonly IReadOnlyList<IRouteDiscovery> _sources = sources.ToList();
    private readonly string _configPath = SharedPaths.YarpConfigPath;
    private readonly string _domain = config.Value.EffectiveDomain;
    private readonly Lock _lock = new();
    private HashSet<string> _knownSubdomains = new(StringComparer.OrdinalIgnoreCase);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged += OnRoutesChanged;
        }

        WriteConfig();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged -= OnRoutesChanged;
        }

        return Task.CompletedTask;
    }

    private void OnRoutesChanged()
    {
        lock (_lock)
        {
            WriteConfig();
        }
    }

    private void WriteConfig()
    {
        try
        {
            var allRoutes = _sources.SelectMany(s => s.GetRoutes()).ToList();
            var merged = Merge(allRoutes);

            // Notify DNS of new subdomains
            foreach (var route in merged)
            {
                if (_knownSubdomains.Add(route.Subdomain))
                {
                    LogSubdomainDiscovered(logger, route.Subdomain);
                    dnsChannel.Send(new DnsCommand.SubdomainAdded(route.Subdomain));
                }
            }

            var yarpConfig = BuildYarpConfig(merged, _domain);
            var json = yarpConfig.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            var directory = Path.GetDirectoryName(_configPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, overwrite: true);

            LogConfigWritten(logger, _configPath, merged.Count);
        }
        catch (Exception ex)
        {
            LogConfigWriteFailed(logger, _configPath, ex);
        }
    }

    /// <summary>
    /// Builds a YARP-format JSON object from discovered routes.
    /// </summary>
    internal static JsonObject BuildYarpConfig(List<DiscoveredRoute> routes, string domain)
    {
        var routesObj = new JsonObject();
        var clustersObj = new JsonObject();

        foreach (var route in routes)
        {
            var routeId = $"route-{route.Subdomain}";
            var clusterId = $"cluster-{route.Subdomain}";
            var host = route.IsApex ? domain : $"{route.Subdomain}.{domain}";

            routesObj[routeId] = new JsonObject
            {
                ["ClusterId"] = clusterId,
                ["Match"] = new JsonObject
                {
                    ["Hosts"] = new JsonArray(host)
                }
            };

            clustersObj[clusterId] = new JsonObject
            {
                ["Destinations"] = new JsonObject
                {
                    ["default"] = new JsonObject
                    {
                        ["Address"] = route.BackendUrl
                    }
                }
            };
        }

        return new JsonObject
        {
            ["ReverseProxy"] = new JsonObject
            {
                ["Routes"] = routesObj,
                ["Clusters"] = clustersObj
            }
        };
    }

    /// <summary>
    /// Deduplicates routes by subdomain, keeping the first occurrence.
    /// </summary>
    internal static List<DiscoveredRoute> Merge(List<DiscoveredRoute> allRoutes)
    {
        return allRoutes
            .GroupBy(r => r.Subdomain, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged -= OnRoutesChanged;
        }
    }

    [LoggerMessage(EventId = 1700, Level = LogLevel.Information, Message = "YARP config written to {path} with {count} routes")]
    private static partial void LogConfigWritten(ILogger logger, string path, int count);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Warning, Message = "Failed to write YARP config to {path}")]
    private static partial void LogConfigWriteFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Information, Message = "New subdomain discovered: {subdomain}")]
    private static partial void LogSubdomainDiscovered(ILogger logger, string subdomain);
}

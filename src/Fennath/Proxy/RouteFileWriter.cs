using Fennath.Core;
using Fennath.Discovery;

namespace Fennath.Proxy;

/// <summary>
/// Watches for route changes from DockerRouteDiscovery and writes the current
/// set of subdomains to routes.json on the shared volume. The sidecar reads
/// this file to know which DNS A records to create.
/// </summary>
public sealed partial class RouteFileWriter(
    IEnumerable<IRouteDiscovery> sources,
    ILogger<RouteFileWriter> logger) : IHostedService, IDisposable
{
    private readonly IReadOnlyList<IRouteDiscovery> _sources = sources.ToList();
    private readonly string _manifestPath = SharedPaths.RoutesManifestPath;
    private readonly ILogger<RouteFileWriter> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged += OnRoutesChanged;
        }

        WriteManifest();
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
        WriteManifest();
    }

    private void WriteManifest()
    {
        try
        {
            var subdomains = _sources
                .SelectMany(s => s.GetRoutes())
                .Select(r => r.Subdomain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var manifest = new RoutesManifest
            {
                Timestamp = DateTimeOffset.UtcNow,
                Subdomains = subdomains
            };

            var directory = Path.GetDirectoryName(_manifestPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _manifestPath + ".tmp";
            File.WriteAllText(tempPath, manifest.ToJson());
            File.Move(tempPath, _manifestPath, overwrite: true);
            LogManifestWritten(_logger, _manifestPath, subdomains.Count);
        }
        catch (Exception ex)
        {
            LogManifestWriteFailed(_logger, _manifestPath, ex);
        }
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged -= OnRoutesChanged;
        }
    }

    [LoggerMessage(EventId = 1410, Level = LogLevel.Information, Message = "Routes manifest written to {path} with {count} subdomains")]
    private static partial void LogManifestWritten(ILogger logger, string path, int count);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Warning, Message = "Failed to write routes manifest to {path}")]
    private static partial void LogManifestWriteFailed(ILogger logger, string path, Exception ex);
}

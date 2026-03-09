using Fennath.Core;
using Fennath.Sidecar.Dns;

namespace Fennath.Sidecar;

/// <summary>
/// Watches the routes manifest file (routes.json) written by the proxy container.
/// When new subdomains appear, sends DnsCommand.SubdomainAdded to the DNS
/// reconciliation service.
/// </summary>
public sealed partial class RouteFileWatcher(
    DnsCommandChannel DnsChannel,
    ILogger<RouteFileWatcher> Logger) : BackgroundService
{
    private HashSet<string> _knownSubdomains = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var manifestPath = SharedPaths.RoutesManifestPath;
        var directory = Path.GetDirectoryName(manifestPath)!;
        var fileName = Path.GetFileName(manifestPath);

        Directory.CreateDirectory(directory);

        // Load any existing manifest
        ProcessManifest(manifestPath);

        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stoppingToken.Register(() => tcs.TrySetCanceled());

        watcher.Changed += (_, e) => SafeProcessManifest(e.FullPath, stoppingToken);
        watcher.Created += (_, e) => SafeProcessManifest(e.FullPath, stoppingToken);

        LogWatchingStarted(Logger, manifestPath);

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private void SafeProcessManifest(string path, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        ProcessManifest(path);
    }

    private void ProcessManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var manifest = RoutesManifest.FromJson(json);
            if (manifest is null)
            {
                return;
            }

            var currentSubdomains = new HashSet<string>(
                manifest.Subdomains, StringComparer.OrdinalIgnoreCase);

            // Find new subdomains
            foreach (var subdomain in currentSubdomains)
            {
                if (_knownSubdomains.Add(subdomain))
                {
                    LogSubdomainDiscovered(Logger, subdomain);
                    DnsChannel.Send(new DnsCommand.SubdomainAdded(subdomain));
                }
            }

            LogManifestProcessed(Logger, path, manifest.Subdomains.Count);
        }
        catch (Exception ex)
        {
            LogManifestReadFailed(Logger, path, ex);
        }
    }

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "Watching routes manifest for changes: {path}")]
    private static partial void LogWatchingStarted(ILogger logger, string path);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information, Message = "New subdomain discovered from proxy: {subdomain}")]
    private static partial void LogSubdomainDiscovered(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Debug, Message = "Routes manifest processed: {path} ({count} subdomains)")]
    private static partial void LogManifestProcessed(ILogger logger, string path, int count);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Warning, Message = "Failed to read routes manifest from {path}")]
    private static partial void LogManifestReadFailed(ILogger logger, string path, Exception ex);
}

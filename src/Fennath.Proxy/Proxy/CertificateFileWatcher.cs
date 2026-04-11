using Fennath.Certificates;

namespace Fennath.Proxy;

/// <summary>
/// Watches the certificate storage directory for PFX file changes.
/// When operators write new certificates, this service detects the changes
/// and reloads all certificates into the CertificateStore for zero-downtime rotation.
/// Supports multi-operator deployments by watching subdirectories recursively.
/// </summary>
public sealed partial class CertificateFileWatcher(
    CertificateStore CertStore,
    ILogger<CertificateFileWatcher> Logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = CertStore.GetStoragePath();

        while (!Directory.Exists(directory))
        {
            LogWaitingForDirectory(Logger, directory);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        using var watcher = new FileSystemWatcher(directory, "*.pfx")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stoppingToken.Register(() => tcs.TrySetCanceled());

        watcher.Changed += OnCertificateFileChanged;
        watcher.Created += OnCertificateFileChanged;
        watcher.Renamed += OnCertificateFileRenamed;

        // Initial reload catches certs written before watcher attached
        CertStore.ReloadFromDisk();
        LogWatchingStarted(Logger, directory);

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private void OnCertificateFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            CertStore.ReloadFromDisk();
            LogCertificateReloaded(Logger, e.FullPath);
        }
        catch (Exception ex)
        {
            LogReloadFailed(Logger, e.FullPath, ex);
        }
    }

    private void OnCertificateFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            CertStore.ReloadFromDisk();
            LogCertificateReloaded(Logger, e.FullPath);
        }
        catch (Exception ex)
        {
            LogReloadFailed(Logger, e.FullPath, ex);
        }
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "Watching certificate directory for changes: {path}")]
    private static partial void LogWatchingStarted(ILogger logger, string path);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Certificate reloaded from {path}")]
    private static partial void LogCertificateReloaded(ILogger logger, string path);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning, Message = "Failed to reload certificate from {path}")]
    private static partial void LogReloadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Information, Message = "Waiting for directory to be created: {directory}")]
    private static partial void LogWaitingForDirectory(ILogger logger, string directory);
}

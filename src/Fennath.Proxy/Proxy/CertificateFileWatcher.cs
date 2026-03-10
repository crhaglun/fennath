using Fennath.Certificates;

namespace Fennath.Proxy;

/// <summary>
/// Watches the certificate PFX file on the shared volume for changes.
/// When the operator writes a new certificate, this service detects the change
/// and reloads it into the in-memory CertificateStore for zero-downtime rotation.
/// </summary>
public sealed partial class CertificateFileWatcher(
    CertificateStore CertStore,
    ILogger<CertificateFileWatcher> Logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var certPath = CertStore.GetCertificatePath();
        var directory = Path.GetDirectoryName(certPath)!;
        var fileName = Path.GetFileName(certPath);

        Directory.CreateDirectory(directory);

        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stoppingToken.Register(() => tcs.TrySetCanceled());

        watcher.Changed += OnCertificateFileChanged;
        watcher.Created += OnCertificateFileChanged;

        LogWatchingStarted(Logger, certPath);

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

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "Watching certificate file for changes: {path}")]
    private static partial void LogWatchingStarted(ILogger logger, string path);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Certificate reloaded from {path}")]
    private static partial void LogCertificateReloaded(ILogger logger, string path);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning, Message = "Failed to reload certificate from {path}")]
    private static partial void LogReloadFailed(ILogger logger, string path, Exception ex);
}

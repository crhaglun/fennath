namespace Fennath.Proxy.Configuration;

/// <summary>
/// Watches a directory for JSON config files matching a pattern (e.g. <c>yarp-config-*.json</c>),
/// merges them into the .NET configuration system, and triggers reload when files are
/// added, changed, or removed. Operators write per-domain YARP config files to a shared
/// volume; this provider discovers them automatically.
/// </summary>
public sealed class DirectoryJsonConfigurationProvider(string directory, string filePattern) : ConfigurationProvider, IDisposable
{
    private readonly string _directory = directory;
    private readonly string _filePattern = filePattern;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    // Debounce rapid events (e.g. multiple operators writing simultaneously)
    private const int DebounceMs = 250;

    public override void Load()
    {
        Data = BuildData();
        EnsureWatcher();
    }

    private Dictionary<string, string?> BuildData()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_directory))
        {
            return data;
        }

        foreach (var file in Directory.GetFiles(_directory, _filePattern).Order())
        {
            try
            {
                using var stream = File.OpenRead(file);
                var fileConfig = new ConfigurationBuilder()
                    .AddJsonStream(stream)
                    .Build();

                foreach (var (key, value) in fileConfig.AsEnumerable())
                {
                    if (value is not null)
                    {
                        data[key] = value;
                    }
                }
            }
            catch
            {
                // Skip malformed files — other valid files still load.
            }
        }

        return data;
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_directory))
        {
            return;
        }

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(_directory, _filePattern)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
                           | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Error += OnWatcherError;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Ignore temp files from atomic writes
        if (e.Name?.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Watcher may recover on its own; trigger a reload to reconcile state.
        _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        try
        {
            var newData = BuildData();

            // Don't wipe routes if all files suddenly failed to parse
            // (e.g. brief I/O error). Keep last known good config.
            if (newData.Count == 0 && Data.Count > 0
                && Directory.GetFiles(_directory, _filePattern).Length > 0)
            {
                return;
            }

            Data = newData;
            OnReload();
        }
        catch
        {
            // Keep serving last known good config
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}

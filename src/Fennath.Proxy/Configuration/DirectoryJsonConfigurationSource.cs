namespace Fennath.Proxy.Configuration;

/// <summary>
/// Configuration source that discovers and merges all JSON files matching a glob
/// pattern in a directory. Files are watched for changes, and new/deleted files
/// are detected at runtime without requiring a restart.
/// </summary>
public sealed class DirectoryJsonConfigurationSource(string directory, string filePattern) : IConfigurationSource
{
    public string Directory { get; } = directory;
    public string FilePattern { get; } = filePattern;

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DirectoryJsonConfigurationProvider(Directory, FilePattern);
}

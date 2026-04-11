namespace Fennath.Proxy.Configuration;

public static class DirectoryJsonConfigurationExtensions
{
    /// <summary>
    /// Adds all JSON files matching <paramref name="filePattern"/> in <paramref name="directory"/>
    /// as a merged configuration source. Files are watched for changes and new files are
    /// discovered at runtime.
    /// </summary>
    public static IConfigurationBuilder AddJsonDirectory(
        this IConfigurationBuilder builder, string directory, string filePattern = "*.json")
    {
        builder.Add(new DirectoryJsonConfigurationSource(directory, filePattern));
        return builder;
    }
}

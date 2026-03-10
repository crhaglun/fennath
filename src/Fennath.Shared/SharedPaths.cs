namespace Fennath.Core;

/// <summary>
/// Constants for shared volume paths used by both proxy and operator containers.
/// </summary>
public static class SharedPaths
{
    /// <summary>
    /// Root directory of the shared volume between proxy and operator.
    /// </summary>
    public const string SharedVolume = "/data/shared";

    /// <summary>
    /// Directory for certificate files on the shared volume.
    /// </summary>
    public const string CertsDirectory = "/data/shared/certs";

    /// <summary>
    /// Path to the YARP proxy configuration file written by the operator
    /// and read by the proxy via <c>AddJsonFile(reloadOnChange: true)</c>.
    /// </summary>
    public const string YarpConfigPath = "/data/shared/yarp-config.json";
}

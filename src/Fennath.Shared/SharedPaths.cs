namespace Fennath.Core;

/// <summary>
/// Constants for shared volume paths used by both proxy and sidecar containers.
/// </summary>
public static class SharedPaths
{
    /// <summary>
    /// Root directory of the shared volume between proxy and sidecar.
    /// </summary>
    public const string SharedVolume = "/data/shared";

    /// <summary>
    /// Directory for certificate files on the shared volume.
    /// </summary>
    public const string CertsDirectory = "/data/shared/certs";

    /// <summary>
    /// Path to the routes manifest file written by the proxy and read by the sidecar.
    /// </summary>
    public const string RoutesManifestPath = "/data/shared/routes.json";
}

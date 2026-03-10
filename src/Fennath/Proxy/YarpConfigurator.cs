using Fennath.Certificates;

namespace Fennath.Proxy;

/// <summary>
/// Configures the YARP reverse proxy pipeline. Route configuration is loaded
/// from a JSON file on the shared volume, written by the sidecar container.
/// YARP watches for config changes via .NET's built-in file change tokens
/// (<c>reloadOnChange: true</c> on <c>AddJsonFile</c>).
///
/// Docker discovery and DNS/ACME management run in the sidecar container
/// (see ADR-014).
/// </summary>
public static class YarpConfigurator
{
    /// <summary>
    /// Adds YARP reverse proxy that reads route configuration from IConfiguration.
    /// The "ReverseProxy" section is populated from the shared volume JSON file.
    /// </summary>
    public static IServiceCollection AddFennathProxy(this IServiceCollection services,
        IConfiguration configuration)
    {
        // YARP — reads routes/clusters from the "ReverseProxy" config section.
        // The sidecar writes yarp-config.json and .NET's file watcher triggers
        // automatic config reload via IConfiguration change tokens.
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        // Certificate store — reads certs written by the sidecar
        services.AddSingleton<CertificateStore>();
        services.AddHostedService<CertificateFileWatcher>();

        return services;
    }
}

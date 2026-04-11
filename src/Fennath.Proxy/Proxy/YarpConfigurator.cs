using Fennath.Certificates;

namespace Fennath.Proxy;

/// <summary>
/// Configures the YARP reverse proxy pipeline. Route configuration is loaded
/// from JSON files on the shared volume, written by operator container(s).
/// Files matching <c>yarp-config-*.json</c> are auto-discovered and watched
/// for changes via <see cref="DirectoryJsonConfigurationProvider"/>.
///
/// Docker discovery and DNS/ACME management run in the operator container
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
        // The operator writes yarp-config.json and .NET's file watcher triggers
        // automatic config reload via IConfiguration change tokens.
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        // Certificate store — reads certs written by the operator
        services.AddSingleton<CertificateStore>();
        services.AddHostedService<CertificateFileWatcher>();

        return services;
    }
}

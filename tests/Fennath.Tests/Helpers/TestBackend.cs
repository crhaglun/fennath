using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fennath.Tests.Helpers;

/// <summary>
/// A lightweight HTTP backend for integration tests.
/// Creates a real HTTP server on a random port.
/// </summary>
public sealed class TestBackend : IAsyncDisposable
{
    private readonly IHost _host;

    public string Url { get; }

    private TestBackend(IHost host, string url)
    {
        _host = host;
        Url = url;
    }

    public static async Task<TestBackend> CreateAsync(
        Action<IEndpointRouteBuilder>? configureEndpoints = null)
    {
        configureEndpoints ??= endpoints =>
        {
            endpoints.MapGet("/", () => "hello from backend");
            endpoints.MapGet("/api/health", () => "ok");
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(configureEndpoints);
                });
            })
            .Build();

        await host.StartAsync();

        var url = host.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new TestBackend(host, url);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

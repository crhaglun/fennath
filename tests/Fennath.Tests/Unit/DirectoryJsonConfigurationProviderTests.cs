using System.Text.Json;
using Fennath.Proxy.Configuration;
using Microsoft.Extensions.Configuration;

namespace Fennath.Tests.Unit;

public class DirectoryJsonConfigurationProviderTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _tempDir;

    public DirectoryJsonConfigurationProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fennath-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void WriteYarpConfig(string fileName, Dictionary<string, object> routes, Dictionary<string, object> clusters)
    {
        var config = new { ReverseProxy = new { Routes = routes, Clusters = clusters } };
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(Path.Combine(_tempDir, fileName), json);
    }

    private static Dictionary<string, object> SingleRoute(string routeId, string clusterId, string host) =>
        new()
        {
            [routeId] = new { ClusterId = clusterId, Match = new { Hosts = new[] { host } } }
        };

    private static Dictionary<string, object> SingleCluster(string clusterId, string address) =>
        new()
        {
            [clusterId] = new { Destinations = new { @default = new { Address = address } } }
        };

    [Test]
    public async Task Load_DiscoversMatchingFiles()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        var host = config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"];
        await Assert.That(host).IsEqualTo("echo.lab.example.com");
    }

    [Test]
    public async Task Load_MergesMultipleFiles()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        WriteYarpConfig("yarp-config-apps.json",
            SingleRoute("route-apps-wiki", "cluster-apps-wiki", "wiki.apps.example.org"),
            SingleCluster("cluster-apps-wiki", "http://wiki:3000"));

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        var labHost = config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"];
        var appsHost = config["ReverseProxy:Routes:route-apps-wiki:Match:Hosts:0"];

        await Assert.That(labHost).IsEqualTo("echo.lab.example.com");
        await Assert.That(appsHost).IsEqualTo("wiki.apps.example.org");
    }

    [Test]
    public async Task Load_IgnoresNonMatchingFiles()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        File.WriteAllText(Path.Combine(_tempDir, "other-config.json"), """{"Unrelated": "data"}""");

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        await Assert.That(config["Unrelated"]).IsNull();
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"]).IsEqualTo("echo.lab.example.com");
    }

    [Test]
    public async Task Load_ReturnsEmpty_WhenDirectoryDoesNotExist()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(nonExistent, "yarp-config-*.json")
            .Build();

        var value = config["ReverseProxy:Routes:anything"];
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Load_SkipsMalformedFiles()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        File.WriteAllText(Path.Combine(_tempDir, "yarp-config-bad.json"), "not valid json {{{");

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        // Good file still loaded
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"])
            .IsEqualTo("echo.lab.example.com");
    }

    [Test]
    public async Task Reload_DetectsNewFile()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        // Initially only lab route exists
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"])
            .IsEqualTo("echo.lab.example.com");
        await Assert.That(config["ReverseProxy:Routes:route-apps-wiki:Match:Hosts:0"])
            .IsNull();

        // Write a new config file — triggers watcher + debounce
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        config.GetReloadToken().RegisterChangeCallback(_ => reloadTcs.TrySetResult(), null);

        WriteYarpConfig("yarp-config-apps.json",
            SingleRoute("route-apps-wiki", "cluster-apps-wiki", "wiki.apps.example.org"),
            SingleCluster("cluster-apps-wiki", "http://wiki:3000"));

        // Wait for reload (debounce 250ms + file event propagation)
        var completed = await Task.WhenAny(reloadTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await Assert.That(completed).IsEqualTo(reloadTcs.Task);

        await Assert.That(config["ReverseProxy:Routes:route-apps-wiki:Match:Hosts:0"])
            .IsEqualTo("wiki.apps.example.org");
        // Original route still present
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"])
            .IsEqualTo("echo.lab.example.com");
    }

    [Test]
    public async Task Reload_DetectsDeletedFile()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        WriteYarpConfig("yarp-config-apps.json",
            SingleRoute("route-apps-wiki", "cluster-apps-wiki", "wiki.apps.example.org"),
            SingleCluster("cluster-apps-wiki", "http://wiki:3000"));

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        // Both routes present
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"])
            .IsEqualTo("echo.lab.example.com");
        await Assert.That(config["ReverseProxy:Routes:route-apps-wiki:Match:Hosts:0"])
            .IsEqualTo("wiki.apps.example.org");

        // Delete one file
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        config.GetReloadToken().RegisterChangeCallback(_ => reloadTcs.TrySetResult(), null);

        File.Delete(Path.Combine(_tempDir, "yarp-config-apps.json"));

        var completed = await Task.WhenAny(reloadTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await Assert.That(completed).IsEqualTo(reloadTcs.Task);

        // Deleted route gone, remaining route still present
        await Assert.That(config["ReverseProxy:Routes:route-apps-wiki:Match:Hosts:0"])
            .IsNull();
        await Assert.That(config["ReverseProxy:Routes:route-lab-echo:Match:Hosts:0"])
            .IsEqualTo("echo.lab.example.com");
    }

    [Test]
    public async Task Reload_IgnoresTempFiles()
    {
        WriteYarpConfig("yarp-config-lab.json",
            SingleRoute("route-lab-echo", "cluster-lab-echo", "echo.lab.example.com"),
            SingleCluster("cluster-lab-echo", "http://echo:80"));

        var config = new ConfigurationBuilder()
            .AddJsonDirectory(_tempDir, "yarp-config-*.json")
            .Build();

        // Writing a .tmp file should not trigger reload
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        config.GetReloadToken().RegisterChangeCallback(_ => reloadTcs.TrySetResult(), null);

        File.WriteAllText(Path.Combine(_tempDir, "yarp-config-apps.json.tmp"), "temp file");

        // Should NOT reload within the debounce window
        var completed = await Task.WhenAny(reloadTcs.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));
        await Assert.That(completed).IsNotEqualTo(reloadTcs.Task);
    }
}

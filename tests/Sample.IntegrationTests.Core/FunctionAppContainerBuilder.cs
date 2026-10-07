using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Builds an Azure Functions (isolated worker) container from an image produced by <see cref="AppImage"/>.
/// The container joins the emulator network and is considered ready when its health endpoint returns 200.
/// </summary>
public sealed class FunctionAppContainerBuilder
{
    public const int HttpPort = 80;

    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated",
        ["AzureFunctionsJobHost__Logging__Console__IsEnabled"] = "true",
        ["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development",
    };

    private readonly string _image;
    private readonly INetwork _network;
    private string _healthPath = "/api/health";
    private TimeSpan _startupTimeout = TimeSpan.FromMinutes(5);

    public FunctionAppContainerBuilder(string image, INetwork network)
    {
        _image = image;
        _network = network;
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            // The Functions base image is amd64-only. Under qemu emulation on arm64 hosts the .NET runtime
            // aborts (signal 6) unless W^X memory protection is disabled.
            _settings["DOTNET_EnableWriteXorExecute"] = "0";
        }
    }

    public FunctionAppContainerBuilder WithAppSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    public FunctionAppContainerBuilder WithAppSettings(IEnumerable<KeyValuePair<string, string>> settings)
    {
        foreach (var (key, value) in settings)
        {
            _settings[key] = value;
        }

        return this;
    }

    public FunctionAppContainerBuilder WithHealthPath(string path)
    {
        _healthPath = path;
        return this;
    }

    /// <summary>
    /// Cold start of a Functions host can be slow, especially when an amd64 image runs under emulation on arm64.
    /// </summary>
    public FunctionAppContainerBuilder WithStartupTimeout(TimeSpan timeout)
    {
        _startupTimeout = timeout;
        return this;
    }

    public TimeSpan StartupTimeout => _startupTimeout;

    public IContainer Build()
    {
        var builder = new ContainerBuilder(_image)
            .WithName($"functions-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithNetworkAliases(NetworkAliases.FunctionApp)
            .WithPortBinding(HttpPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(
                    r => r.ForPort(HttpPort).ForPath(_healthPath),
                    w => w.WithTimeout(_startupTimeout)));

        foreach (var (key, value) in _settings)
        {
            builder = builder.WithEnvironment(key, value);
        }

        return builder.Build();
    }
}

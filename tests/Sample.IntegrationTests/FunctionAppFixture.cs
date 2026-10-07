using System.Globalization;
using Azure.Messaging.EventHubs.Producer;
using DotNet.Testcontainers.Containers;
using Sample.IntegrationTests.Core;
using WireMock.Client;

[assembly: AssemblyFixture(typeof(Sample.IntegrationTests.FunctionAppFixture))]

namespace Sample.IntegrationTests;

/// <summary>
/// Runs the published container image of the Function App against emulators on a private
/// Docker network. Tests talk to it only through its public interfaces (Event Hubs in, HTTP + Service Bus out).
/// Set <c>RUN_FUNCTION_LOCALLY=true</c> to run the app from the IDE instead and debug it.
/// </summary>
public sealed class FunctionAppFixture : IAsyncLifetime
{
    public const string ImageName = "sample-functions:test";
    public const string EventHubName = "device-events";
    public const string ConsumerGroup = "functions";
    public const string AlertQueue = "device-alerts";
    public const double Threshold = 80;

    private IContainer? _functionApp;

    public EmulatorEnvironment Environment { get; } = new EmulatorEnvironmentBuilder()
        .WithEventHub(EventHubName, partitionCount: 2, ConsumerGroup)
        .WithServiceBusQueue(AlertQueue)
        .WithCosmosContainer("devices", "twins", "/deviceId")
        .WithWireMock()
        .WithLogger(Log)
        .Build();

    public HttpClient Api { get; private set; } = null!;

    public EventHubProducerClient Events { get; private set; } = null!;

    public ServiceBusQueueProbe Alerts { get; private set; } = null!;

    public IWireMockAdminApi WireMock => Environment.WireMockAdmin;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectDirectory = RepoPaths.Combine("src", "Sample.Functions");

        // Build the image while the emulators start: both are slow, neither depends on the other.
        var imageTask = TestSettings.RunFunctionLocally
            ? Task.CompletedTask
            : AppImage.EnsureBuiltAsync(ImageName, projectDirectory, Log, ct);
        await Task.WhenAll(imageTask, Environment.StartAsync(ct));

        Uri baseAddress;
        if (TestSettings.RunFunctionLocally)
        {
            var path = LocalFunctionHost.WriteLocalSettings(projectDirectory, AppSettings(Environment.Host));
            Log($"RUN_FUNCTION_LOCALLY=true: wrote {path}. Start Sample.Functions in your IDE (F5).");
            baseAddress = LocalFunctionHost.DefaultBaseAddress;
            await LocalFunctionHost.WaitUntilHealthyAsync(new Uri(baseAddress, "/api/health"), TestSettings.LocalFunctionStartTimeout, Log, ct);
        }
        else
        {
            _functionApp = new FunctionAppContainerBuilder(ImageName, Environment.Network)
                .WithAppSettings(AppSettings(Environment.InNetwork))
                .Build();
            Environment.TrackForLogs("functions", _functionApp);
            Log("Starting function app container...");
            await _functionApp.StartAsync(ct);
            baseAddress = new Uri($"http://{_functionApp.Hostname}:{_functionApp.GetMappedPublicPort(FunctionAppContainerBuilder.HttpPort)}");
            Log($"Function app ready at {baseAddress}.");
        }

        Api = new HttpClient { BaseAddress = baseAddress };
        Events = new EventHubProducerClient(Environment.Host.EventHubs, EventHubName);
        Alerts = new ServiceBusQueueProbe(Environment.Host.ServiceBus, AlertQueue);
    }

    public async ValueTask DisposeAsync()
    {
        await Environment.SaveLogsAsync(RepoPaths.ArtifactsDirectory("integration"));
        Api?.Dispose();
        if (Events is not null)
        {
            await Events.DisposeAsync();
        }

        if (Alerts is not null)
        {
            await Alerts.DisposeAsync();
        }

        if (_functionApp is not null)
        {
            await _functionApp.DisposeAsync();
        }

        await Environment.DisposeAsync();
    }

    /// <summary>
    /// The app's configuration, generated from the running emulators. The same values feed the container
    /// (in-network endpoints) and local.settings.json for IDE debugging (host endpoints).
    /// </summary>
    private static Dictionary<string, string> AppSettings(EmulatorEndpoints endpoints) => new()
    {
        ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated",
        ["AzureWebJobsStorage"] = endpoints.Storage,
        ["EventHubConnection"] = endpoints.EventHubs,
        ["EventHubName"] = EventHubName,
        ["EventHubConsumerGroup"] = ConsumerGroup,
        ["CosmosDbConnection"] = endpoints.Cosmos,
        ["CosmosDbDatabase"] = "devices",
        ["CosmosDbContainer"] = "twins",
        ["CosmosDbUseEmulator"] = "true",
        ["ServiceBusConnection"] = endpoints.ServiceBus,
        ["AlertQueueName"] = AlertQueue,
        ["EnrichmentApiBaseUrl"] = endpoints.WireMock,
        ["TemperatureAlertThreshold"] = Threshold.ToString(CultureInfo.InvariantCulture),
    };

    private static void Log(string message) => Console.WriteLine($"[integration] {message}");
}

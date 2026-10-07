using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.EventHubs.Consumer;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Azure.Cosmos;
using Testcontainers.Azurite;
using Testcontainers.EventHubs;
using WireMock.Client;
using WireMock.Net.Testcontainers;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Starts a set of Azure emulators on one private Docker network and exposes their connection settings.
/// Independent containers start in parallel; dependants (Event Hubs → Azurite, Service Bus → SQL) start after.
/// </summary>
public sealed class EmulatorEnvironment : IAsyncDisposable
{
    private const int CosmosGatewayPort = 8081;
    private const int CosmosHealthPort = 8080;
    private const int AmqpPort = 5672;
    private const int SqlPort = 1433;

    private readonly bool _useAzurite;
    private readonly IReadOnlyList<EventHubDefinition> _eventHubs;
    private readonly IReadOnlyList<string> _serviceBusQueues;
    private readonly IReadOnlyList<CosmosContainerDefinition> _cosmosContainers;
    private readonly bool _useWireMock;
    private readonly Action<string> _log;
    private readonly List<(string Name, IContainer Container)> _trackedContainers = [];

    private AzuriteContainer? _azurite;
    private EventHubsContainer? _eventHubsContainer;
    private IContainer? _sql;
    private IContainer? _serviceBus;
    private IContainer? _cosmos;
    private WireMockContainer? _wireMock;

    internal EmulatorEnvironment(
        bool useAzurite,
        IReadOnlyList<EventHubDefinition> eventHubs,
        IReadOnlyList<string> serviceBusQueues,
        IReadOnlyList<CosmosContainerDefinition> cosmosContainers,
        bool useWireMock,
        Action<string> log)
    {
        _useAzurite = useAzurite;
        _eventHubs = eventHubs;
        _serviceBusQueues = serviceBusQueues;
        _cosmosContainers = cosmosContainers;
        _useWireMock = useWireMock;
        _log = log;

        Network = new NetworkBuilder().WithName($"tc-it-{Guid.NewGuid():N}").Build();
    }

    public INetwork Network { get; }

    /// <summary>Endpoints reachable from the test process / host machine.</summary>
    public EmulatorEndpoints Host { get; private set; } = new(null, null, null, null, null);

    /// <summary>Endpoints reachable from other containers on <see cref="Network"/>.</summary>
    public EmulatorEndpoints InNetwork { get; private set; } = new(null, null, null, null, null);

    /// <summary>Admin client for configuring WireMock stubs from tests.</summary>
    public IWireMockAdminApi WireMockAdmin => _wireMock?.CreateWireMockAdminClient()
        ?? throw new InvalidOperationException("WireMock is not enabled.");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await Network.CreateAsync(cancellationToken);

        // Stage 1: containers without dependencies, in parallel.
        var stage1 = new List<Task>();
        if (_useAzurite)
        {
            _azurite = new AzuriteBuilder(ContainerImages.Azurite)
                .WithNetwork(Network)
                .WithNetworkAliases(NetworkAliases.Azurite)
                .WithCommand("--skipApiVersionCheck")
                .Build();
            stage1.Add(StartAsync("azurite", _azurite, cancellationToken));
        }

        if (_serviceBusQueues.Count > 0)
        {
            _sql = BuildSqlContainer();
            stage1.Add(StartAsync("sql", _sql, cancellationToken));
        }

        if (_cosmosContainers.Count > 0)
        {
            _cosmos = BuildCosmosContainer();
            stage1.Add(StartAsync("cosmos", _cosmos, cancellationToken));
        }

        if (_useWireMock)
        {
            _wireMock = new WireMockContainerBuilder()
                .WithImage(ContainerImages.WireMock)
                .WithNetwork(Network)
                .WithNetworkAliases(NetworkAliases.WireMock)
                .Build();
            stage1.Add(StartAsync("wiremock", _wireMock, cancellationToken));
        }

        await Task.WhenAll(stage1);

        // Stage 2: containers that depend on stage 1, in parallel.
        var stage2 = new List<Task>();
        if (_eventHubs.Count > 0)
        {
            _eventHubsContainer = new EventHubsBuilder(ContainerImages.EventHubs)
                .WithAcceptLicenseAgreement(true)
                .WithAzuriteContainer(Network, _azurite!, NetworkAliases.Azurite)
                .WithNetworkAliases(NetworkAliases.EventHubs)
                .WithConfigurationBuilder(BuildEventHubsConfiguration())
                .Build();
            stage2.Add(StartAsync("eventhubs", _eventHubsContainer, cancellationToken));
        }

        if (_serviceBusQueues.Count > 0)
        {
            _serviceBus = BuildServiceBusContainer();
            stage2.Add(StartAsync("servicebus", _serviceBus, cancellationToken));
        }

        await Task.WhenAll(stage2);

        if (_cosmos is not null)
        {
            await ProvisionCosmosAsync(cancellationToken);
        }

        Host = BuildHostEndpoints();
        InNetwork = BuildInNetworkEndpoints();
        _log($"Emulator environment ready in {stopwatch.Elapsed.TotalSeconds:F1}s.");
    }

    /// <summary>Creates a Cosmos client configured for the emulator (Gateway mode, no endpoint discovery).</summary>
    public CosmosClient CreateCosmosClient(JsonSerializerOptions? serializerOptions = null) =>
        new(Host.Cosmos, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            LimitToEndpoint = true,
            UseSystemTextJsonSerializerWithOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web),
        });

    /// <summary>Adds an application container so its logs are captured by <see cref="SaveLogsAsync"/>.</summary>
    public void TrackForLogs(string name, IContainer container)
    {
        lock (_trackedContainers)
        {
            _trackedContainers.Add((name, container));
        }
    }

    /// <summary>Writes stdout/stderr of every container to <paramref name="directory"/>. Call on teardown.</summary>
    public async Task SaveLogsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        (string Name, IContainer Container)[] tracked;
        lock (_trackedContainers)
        {
            tracked = [.. _trackedContainers];
        }

        foreach (var (name, container) in tracked)
        {
            try
            {
                var (stdout, stderr) = await container.GetLogsAsync();
                await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.log"), $"{stdout}\n----- STDERR -----\n{stderr}");
            }
            catch (Exception ex)
            {
                _log($"Could not read logs for {name}: {ex.Message}");
            }
        }

        _log($"Container logs written to {directory}");
    }

    public async ValueTask DisposeAsync()
    {
        // Dependants first, then their dependencies, then the network.
        foreach (var container in new IContainer?[] { _serviceBus, _eventHubsContainer, _sql, _azurite, _cosmos, _wireMock })
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }
        }

        await Network.DisposeAsync();
    }

    private async Task StartAsync(string name, IContainer container, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _log($"Starting {name}...");
        lock (_trackedContainers)
        {
            _trackedContainers.Add((name, container));
        }

        await container.StartAsync(cancellationToken);
        _log($"Started {name} in {stopwatch.Elapsed.TotalSeconds:F1}s.");
    }

    private IContainer BuildSqlContainer() =>
        new ContainerBuilder(ContainerImages.Sql)
            .WithNetwork(Network)
            .WithNetworkAliases(NetworkAliases.Sql)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", EmulatorCredentials.SqlPassword)
            .WithPortBinding(SqlPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready"))
            .Build();

    private IContainer BuildServiceBusContainer()
    {
        var config = new
        {
            UserConfig = new
            {
                Namespaces = new[]
                {
                    new
                    {
                        Name = "sbemulatorns",
                        Queues = _serviceBusQueues.Select(q => new { Name = q }).ToArray(),
                        Topics = Array.Empty<object>(),
                    },
                },
                Logging = new { Type = "Console" },
            },
        };

        return new ContainerBuilder(ContainerImages.ServiceBus)
            .WithNetwork(Network)
            .WithNetworkAliases(NetworkAliases.ServiceBus)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", NetworkAliases.Sql)
            .WithEnvironment("MSSQL_SA_PASSWORD", EmulatorCredentials.SqlPassword)
            .WithEnvironment("SQL_WAIT_INTERVAL", "0")
            .WithPortBinding(AmqpPort, true)
            .WithResourceMapping(JsonSerializer.SerializeToUtf8Bytes(config), "/ServiceBus_Emulator/ConfigFiles/Config.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Emulator Service is Successfully Up!"))
            .Build();
    }

    private IContainer BuildCosmosContainer() =>
        new ContainerBuilder(ContainerImages.Cosmos)
            .WithNetwork(Network)
            .WithNetworkAliases(NetworkAliases.Cosmos)
            .WithPortBinding(CosmosGatewayPort, true)
            .WithPortBinding(CosmosHealthPort, true)
            .WithCommand("--protocol", "http", "--enable-explorer", "false")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(CosmosHealthPort).ForPath("/ready")))
            .Build();

    private EventHubsServiceConfiguration BuildEventHubsConfiguration()
    {
        var configuration = EventHubsServiceConfiguration.Create();
        foreach (var hub in _eventHubs)
        {
            string[] groups = [EventHubConsumerClient.DefaultConsumerGroupName, .. hub.ConsumerGroups];
            configuration = configuration.WithEntity(hub.Name, hub.PartitionCount, groups);
        }

        return configuration;
    }

    private async Task ProvisionCosmosAsync(CancellationToken cancellationToken)
    {
        Host = BuildHostEndpoints();
        using var client = CreateCosmosClient();
        foreach (var definition in _cosmosContainers)
        {
            var database = (await client.CreateDatabaseIfNotExistsAsync(definition.Database, cancellationToken: cancellationToken)).Database;
            await database.CreateContainerIfNotExistsAsync(definition.Container, definition.PartitionKeyPath, cancellationToken: cancellationToken);
            _log($"Provisioned Cosmos container {definition.Database}/{definition.Container}.");
        }
    }

    private EmulatorEndpoints BuildHostEndpoints() => new(
        StorageConnectionString: _azurite?.GetConnectionString(),
        EventHubsConnectionString: _eventHubsContainer?.GetConnectionString(),
        ServiceBusConnectionString: _serviceBus is null ? null
            : ServiceBusConnectionString($"{_serviceBus.Hostname}:{_serviceBus.GetMappedPublicPort(AmqpPort)}"),
        CosmosConnectionString: _cosmos is null ? null
            : CosmosConnectionString($"http://{_cosmos.Hostname}:{_cosmos.GetMappedPublicPort(CosmosGatewayPort)}/"),
        WireMockUrl: _wireMock?.GetPublicUrl().TrimEnd('/'));

    private EmulatorEndpoints BuildInNetworkEndpoints() => new(
        StorageConnectionString: _azurite is null ? null
            : $"DefaultEndpointsProtocol=http;AccountName={EmulatorCredentials.AzuriteAccountName};AccountKey={EmulatorCredentials.AzuriteAccountKey};" +
              $"BlobEndpoint=http://{NetworkAliases.Azurite}:10000/{EmulatorCredentials.AzuriteAccountName};" +
              $"QueueEndpoint=http://{NetworkAliases.Azurite}:10001/{EmulatorCredentials.AzuriteAccountName};" +
              $"TableEndpoint=http://{NetworkAliases.Azurite}:10002/{EmulatorCredentials.AzuriteAccountName};",
        EventHubsConnectionString: _eventHubsContainer is null ? null : ServiceBusConnectionString(NetworkAliases.EventHubs),
        ServiceBusConnectionString: _serviceBus is null ? null : ServiceBusConnectionString(NetworkAliases.ServiceBus),
        CosmosConnectionString: _cosmos is null ? null : CosmosConnectionString($"http://{NetworkAliases.Cosmos}:{CosmosGatewayPort}/"),
        WireMockUrl: _wireMock is null ? null : $"http://{NetworkAliases.WireMock}");

    // Event Hubs and Service Bus emulators share the same connection string shape.
    private static string ServiceBusConnectionString(string hostAndPort) =>
        $"Endpoint=sb://{hostAndPort};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey={EmulatorCredentials.ServiceBusSasKey};UseDevelopmentEmulator=true;";

    private static string CosmosConnectionString(string endpoint) =>
        $"AccountEndpoint={endpoint};AccountKey={EmulatorCredentials.CosmosKey};";
}

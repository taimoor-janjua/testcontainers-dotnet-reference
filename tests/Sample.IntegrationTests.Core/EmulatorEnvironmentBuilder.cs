namespace Sample.IntegrationTests.Core;

public sealed record EventHubDefinition(string Name, int PartitionCount, IReadOnlyList<string> ConsumerGroups);

public sealed record CosmosContainerDefinition(string Database, string Container, string PartitionKeyPath);

/// <summary>
/// Declarative description of the emulators a test suite needs. Nothing here knows about the application's
/// domain: suites state <i>what</i> they need (hubs, queues, containers) and the environment works out <i>how</i>.
/// </summary>
public sealed class EmulatorEnvironmentBuilder
{
    private readonly List<EventHubDefinition> _eventHubs = [];
    private readonly List<string> _serviceBusQueues = [];
    private readonly List<CosmosContainerDefinition> _cosmosContainers = [];
    private bool _azurite;
    private bool _wireMock;
    private Action<string> _log = Console.WriteLine;

    public EmulatorEnvironmentBuilder WithAzurite()
    {
        _azurite = true;
        return this;
    }

    /// <summary>Adds an Event Hub. Also enables Azurite, which the Event Hubs emulator uses for metadata.</summary>
    public EmulatorEnvironmentBuilder WithEventHub(string name, int partitionCount = 2, params string[] consumerGroups)
    {
        _azurite = true;
        _eventHubs.Add(new EventHubDefinition(name, partitionCount, consumerGroups));
        return this;
    }

    public EmulatorEnvironmentBuilder WithServiceBusQueue(string queueName)
    {
        _serviceBusQueues.Add(queueName);
        return this;
    }

    public EmulatorEnvironmentBuilder WithCosmosContainer(string database, string container, string partitionKeyPath = "/id")
    {
        _cosmosContainers.Add(new CosmosContainerDefinition(database, container, partitionKeyPath));
        return this;
    }

    /// <summary>Adds a WireMock.Net container for stubbing outbound HTTP dependencies.</summary>
    public EmulatorEnvironmentBuilder WithWireMock()
    {
        _wireMock = true;
        return this;
    }

    public EmulatorEnvironmentBuilder WithLogger(Action<string> log)
    {
        _log = log;
        return this;
    }

    public EmulatorEnvironment Build() => new(
        _azurite,
        [.. _eventHubs],
        [.. _serviceBusQueues],
        [.. _cosmosContainers],
        _wireMock,
        _log);
}

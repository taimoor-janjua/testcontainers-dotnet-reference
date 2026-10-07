namespace Sample.IntegrationTests.Core;

/// <summary>
/// Connection settings for the emulators. The environment exposes two views of the same containers:
/// <list type="bullet">
/// <item><see cref="EmulatorEnvironment.Host"/>: reachable from the test process (and an F5-debugged function) via mapped ports.</item>
/// <item><see cref="EmulatorEnvironment.InNetwork"/>: reachable from other containers on the shared Docker network via aliases.</item>
/// </list>
/// </summary>
public sealed record EmulatorEndpoints(
    string? StorageConnectionString,
    string? EventHubsConnectionString,
    string? ServiceBusConnectionString,
    string? CosmosConnectionString,
    string? WireMockUrl)
{
    public string Storage => StorageConnectionString ?? throw NotEnabled("Azurite");
    public string EventHubs => EventHubsConnectionString ?? throw NotEnabled("Event Hubs");
    public string ServiceBus => ServiceBusConnectionString ?? throw NotEnabled("Service Bus");
    public string Cosmos => CosmosConnectionString ?? throw NotEnabled("Cosmos DB");
    public string WireMock => WireMockUrl ?? throw NotEnabled("WireMock");

    private static InvalidOperationException NotEnabled(string name)
        => new($"{name} is not part of this EmulatorEnvironment. Enable it on the EmulatorEnvironmentBuilder.");
}

public static class NetworkAliases
{
    public const string Azurite = "azurite";
    public const string EventHubs = "eventhubs";
    public const string ServiceBus = "servicebus";
    public const string Sql = "sql";
    public const string Cosmos = "cosmos";
    public const string WireMock = "wiremock";
    public const string FunctionApp = "functions";
}

/// <summary>Well-known, publicly documented emulator credentials. Not secrets.</summary>
public static class EmulatorCredentials
{
    public const string CosmosKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    public const string AzuriteAccountName = "devstoreaccount1";
    public const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    public const string SqlPassword = "Testcontainers!123";
    public const string ServiceBusSasKey = "SAS_KEY_VALUE";
}

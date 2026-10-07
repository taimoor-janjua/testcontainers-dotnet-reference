namespace Sample.Application;

/// <summary>
/// Application settings. Keys are flat so they map 1:1 to Azure Functions app settings / environment variables.
/// </summary>
public sealed class SampleOptions
{
    public string CosmosDbConnection { get; set; } = string.Empty;
    public string CosmosDbDatabase { get; set; } = "devices";
    public string CosmosDbContainer { get; set; } = "twins";

    /// <summary>
    /// When true, the Cosmos client uses Gateway mode and does not follow the account's advertised endpoints.
    /// Required for the Cosmos DB emulator running in a container.
    /// </summary>
    public bool CosmosDbUseEmulator { get; set; }

    public string ServiceBusConnection { get; set; } = string.Empty;
    public string AlertQueueName { get; set; } = "device-alerts";

    public string EnrichmentApiBaseUrl { get; set; } = string.Empty;
    public double TemperatureAlertThreshold { get; set; } = 80;
}

namespace Sample.Domain;

/// <summary>Telemetry event emitted by a device and delivered through Event Hubs.</summary>
public sealed record DeviceEvent(string DeviceId, double Temperature, DateTimeOffset Timestamp);

/// <summary>Reference data about a device, served by an external enrichment API.</summary>
public sealed record DeviceMetadata(string Region, string Model)
{
    public static DeviceMetadata Unknown { get; } = new("unknown", "unknown");
}

/// <summary>Message published to Service Bus when a device enters the alert state.</summary>
public sealed record DeviceAlert(string DeviceId, double Temperature, double Threshold, DateTimeOffset RaisedAt);

public static class DeviceStatus
{
    public const string Normal = "Normal";
    public const string Alert = "Alert";
}

/// <summary>Current state of a device ("digital twin"), persisted in Cosmos DB, partitioned by <see cref="DeviceId"/>.</summary>
public sealed class DeviceTwin
{
    public required string Id { get; init; }
    public required string DeviceId { get; init; }
    public string Region { get; set; } = DeviceMetadata.Unknown.Region;
    public string Model { get; set; } = DeviceMetadata.Unknown.Model;
    public double LastTemperature { get; set; }
    public string Status { get; set; } = DeviceStatus.Normal;
    public long EventCount { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

namespace Sample.IntegrationTests;

// The test suite owns its view of the app's public contracts instead of referencing src/.
// If the app changes a contract incompatibly, these tests fail, which is exactly what a consumer would see.

public sealed record DeviceEventMessage(string DeviceId, double Temperature, DateTimeOffset Timestamp);

public sealed record TwinResponse(
    string DeviceId,
    string Region,
    string Model,
    double LastTemperature,
    string Status,
    long EventCount,
    DateTimeOffset LastEventAt);

public sealed record AlertMessage(string DeviceId, double Temperature, double Threshold, DateTimeOffset RaisedAt);

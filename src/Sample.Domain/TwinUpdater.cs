namespace Sample.Domain;

public sealed record TwinUpdateResult(DeviceTwin Twin, DeviceAlert? Alert);

/// <summary>
/// Pure business rules for applying a device event to a twin. No I/O, so it is unit-tested without containers.
/// </summary>
public static class TwinUpdater
{
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    /// <item>Every event increments <see cref="DeviceTwin.EventCount"/>.</item>
    /// <item>Only the newest event (by timestamp) updates temperature and status, so out-of-order delivery is safe.</item>
    /// <item>An alert is raised only on the transition from <see cref="DeviceStatus.Normal"/> to <see cref="DeviceStatus.Alert"/>.</item>
    /// </list>
    /// </remarks>
    public static TwinUpdateResult Apply(
        DeviceTwin? current,
        DeviceEvent deviceEvent,
        DeviceMetadata metadata,
        double alertThreshold,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceEvent);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceEvent.DeviceId);

        var twin = current ?? new DeviceTwin { Id = deviceEvent.DeviceId, DeviceId = deviceEvent.DeviceId };
        var previousStatus = current?.Status ?? DeviceStatus.Normal;

        twin.EventCount++;
        twin.Region = metadata.Region;
        twin.Model = metadata.Model;
        twin.UpdatedAt = now;

        var isNewest = current is null || deviceEvent.Timestamp >= twin.LastEventAt;
        if (!isNewest)
        {
            return new TwinUpdateResult(twin, Alert: null);
        }

        twin.LastEventAt = deviceEvent.Timestamp;
        twin.LastTemperature = deviceEvent.Temperature;
        twin.Status = deviceEvent.Temperature > alertThreshold ? DeviceStatus.Alert : DeviceStatus.Normal;

        var alert = previousStatus != DeviceStatus.Alert && twin.Status == DeviceStatus.Alert
            ? new DeviceAlert(twin.DeviceId, deviceEvent.Temperature, alertThreshold, now)
            : null;

        return new TwinUpdateResult(twin, alert);
    }
}

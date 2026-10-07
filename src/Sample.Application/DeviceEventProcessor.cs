using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Domain;

namespace Sample.Application;

/// <summary>Orchestrates enrichment, persistence and alerting for a single device event.</summary>
public sealed class DeviceEventProcessor(
    IDeviceMetadataClient metadataClient,
    ITwinRepository repository,
    IAlertPublisher alertPublisher,
    IOptions<SampleOptions> options,
    TimeProvider timeProvider,
    ILogger<DeviceEventProcessor> logger)
{
    private const int MaxConcurrencyRetries = 10;

    public async Task<TwinUpdateResult> ProcessAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken)
    {
        var metadata = await metadataClient.GetAsync(deviceEvent.DeviceId, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var existing = await repository.GetAsync(deviceEvent.DeviceId, cancellationToken);
            var result = TwinUpdater.Apply(
                existing?.Twin,
                deviceEvent,
                metadata,
                options.Value.TemperatureAlertThreshold,
                timeProvider.GetUtcNow());

            try
            {
                await repository.SaveAsync(result.Twin, existing?.ETag, cancellationToken);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxConcurrencyRetries)
            {
                logger.LogDebug("Concurrency conflict for {DeviceId}, retry {Attempt}", deviceEvent.DeviceId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 50 * attempt)), cancellationToken);
                continue;
            }

            if (result.Alert is not null)
            {
                await alertPublisher.PublishAsync(result.Alert, cancellationToken);
                logger.LogInformation("Alert raised for {DeviceId} at {Temperature}", deviceEvent.DeviceId, deviceEvent.Temperature);
            }

            return result;
        }
    }
}

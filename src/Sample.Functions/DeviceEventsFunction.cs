using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Sample.Application;
using Sample.Domain;

namespace Sample.Functions;

public sealed class DeviceEventsFunction(DeviceEventProcessor processor, ILogger<DeviceEventsFunction> logger)
{
    [Function("ProcessDeviceEvents")]
    public async Task RunAsync(
        [EventHubTrigger("%EventHubName%", Connection = "EventHubConnection", ConsumerGroup = "%EventHubConsumerGroup%")]
        string[] messages,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        foreach (var message in messages)
        {
            try
            {
                var deviceEvent = JsonSerializer.Deserialize<DeviceEvent>(message, JsonSerializerOptions.Web)
                    ?? throw new JsonException("Empty device event.");

                var result = await processor.ProcessAsync(deviceEvent, cancellationToken);
                logger.LogInformation(
                    "Processed event for {DeviceId}: status={Status} count={Count}",
                    deviceEvent.DeviceId, result.Twin.Status, result.Twin.EventCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process message {Message}", message);
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more device events failed.", failures);
        }
    }
}

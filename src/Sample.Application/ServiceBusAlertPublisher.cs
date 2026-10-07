using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Sample.Domain;

namespace Sample.Application;

public interface IAlertPublisher
{
    Task PublishAsync(DeviceAlert alert, CancellationToken cancellationToken);
}

public sealed class ServiceBusAlertPublisher(ServiceBusSender sender) : IAlertPublisher
{
    public Task PublishAsync(DeviceAlert alert, CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(alert, JsonSerializerOptions.Web))
        {
            ContentType = "application/json",
            Subject = nameof(DeviceAlert),
            CorrelationId = alert.DeviceId,
        };

        return sender.SendMessageAsync(message, cancellationToken);
    }
}

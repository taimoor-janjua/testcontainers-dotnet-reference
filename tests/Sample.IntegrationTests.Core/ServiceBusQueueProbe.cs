using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Drains a Service Bus queue into memory so that parallel tests can each wait for "their" message
/// (matched by a predicate, e.g. correlation id) without stealing messages from one another.
/// </summary>
public sealed class ServiceBusQueueProbe : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusReceiver _receiver;
    private readonly List<ServiceBusReceivedMessage> _received = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServiceBusQueueProbe(string connectionString, string queueName)
    {
        _client = new ServiceBusClient(connectionString);
        _receiver = _client.CreateReceiver(queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
    }

    public async Task<ServiceBusReceivedMessage> WaitForMessageAsync(
        Func<ServiceBusReceivedMessage, bool> predicate,
        string because,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? Eventually.DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < limit)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var match = _received.FirstOrDefault(predicate);
                if (match is not null)
                {
                    return match;
                }

                var batch = await _receiver.ReceiveMessagesAsync(maxMessages: 50, maxWaitTime: TimeSpan.FromSeconds(1), cancellationToken);
                _received.AddRange(batch);
            }
            finally
            {
                _gate.Release();
            }
        }

        throw new EventuallyTimeoutException(
            $"No Service Bus message matched within {limit.TotalSeconds:F0}s: {because}. Received {_received.Count} other message(s).",
            inner: null);
    }

    /// <summary>Returns all messages drained so far that match <paramref name="predicate"/>.</summary>
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> DrainAsync(
        Func<ServiceBusReceivedMessage, bool> predicate,
        TimeSpan settleTime,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < settleTime)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _received.AddRange(await _receiver.ReceiveMessagesAsync(50, TimeSpan.FromMilliseconds(500), cancellationToken));
            }
            finally
            {
                _gate.Release();
            }
        }

        return [.. _received.Where(predicate)];
    }

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        await _client.DisposeAsync();
        _gate.Dispose();
    }
}

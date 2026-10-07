using System.Diagnostics;
using System.Text.Json;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Polls an asynchronous probe until a condition holds. This is the only correct way to assert on
/// eventually-consistent, message-driven systems: never use fixed <c>Task.Delay</c> sleeps in tests.
/// On timeout the failure message contains the last observed value, which makes failures diagnosable.
/// </summary>
public static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    public static async Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> condition,
        string because,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;
        T? last = default;
        Exception? lastError = null;
        var attempts = 0;

        while (deadline.Elapsed < limit)
        {
            attempts++;
            try
            {
                last = await probe(cancellationToken);
                lastError = null;
                if (condition(last))
                {
                    return last;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }

            await Task.Delay(interval ?? DefaultInterval, cancellationToken);
        }

        throw new EventuallyTimeoutException(
            $"Condition not met within {limit.TotalSeconds:F0}s after {attempts} attempts: {because}.{Environment.NewLine}" +
            $"Last observed value: {Describe(last)}" +
            (lastError is null ? string.Empty : $"{Environment.NewLine}Last error: {lastError.GetType().Name}: {lastError.Message}"),
            lastError);
    }

    private static string Describe<T>(T? value)
    {
        try
        {
            return value is null ? "<null>" : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (NotSupportedException)
        {
            return value?.ToString() ?? "<null>";
        }
    }
}

public sealed class EventuallyTimeoutException(string message, Exception? inner) : Exception(message, inner);

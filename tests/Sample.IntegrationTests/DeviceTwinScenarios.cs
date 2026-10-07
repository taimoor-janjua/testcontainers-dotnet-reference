using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Sample.IntegrationTests.Core;

namespace Sample.IntegrationTests;

/// <summary>
/// Black-box scenarios: publish to Event Hubs, observe through the HTTP API and the Service Bus alert queue.
/// Every test uses unique device ids, so tests run in parallel against one shared app instance.
/// </summary>
public sealed class DeviceTwinScenarios(FunctionAppFixture app)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var response = await app.Api.GetAsync("/api/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_twin_returns_404()
    {
        using var response = await app.Api.GetAsync($"/api/twins/{NewDeviceId()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Device_event_becomes_queryable_twin()
    {
        var deviceId = NewDeviceId();
        await app.WireMock.StubGetJsonAsync($"/devices/{deviceId}", new { region = "eu-west", model = "TX-100" });

        await PublishAsync(new DeviceEventMessage(deviceId, 21.5, DateTimeOffset.UtcNow));

        var twin = await WaitForTwinAsync(deviceId, t => t.EventCount == 1);
        Assert.Equal("eu-west", twin.Region);
        Assert.Equal("TX-100", twin.Model);
        Assert.Equal(21.5, twin.LastTemperature);
        Assert.Equal("Normal", twin.Status);
    }

    [Fact]
    public async Task High_temperature_raises_alert_on_service_bus()
    {
        var deviceId = NewDeviceId();
        await app.WireMock.StubGetJsonAsync($"/devices/{deviceId}", new { region = "us-east", model = "TX-200" });

        await PublishAsync(new DeviceEventMessage(deviceId, 97.3, DateTimeOffset.UtcNow));

        var message = await app.Alerts.WaitForMessageAsync(m => m.CorrelationId == deviceId, $"an alert for {deviceId}", cancellationToken: Ct);
        var alert = message.Body.ToObjectFromJson<AlertMessage>(Json);
        Assert.Equal(97.3, alert!.Temperature);
        Assert.Equal(FunctionAppFixture.Threshold, alert.Threshold);

        var twin = await WaitForTwinAsync(deviceId, t => t.EventCount == 1);
        Assert.Equal("Alert", twin.Status);
    }

    [Fact]
    public async Task Event_is_processed_despite_transient_enrichment_failures()
    {
        var deviceId = NewDeviceId();
        await app.WireMock.StubGetJsonAfterFailuresAsync($"/devices/{deviceId}", new { region = "ap-south", model = "TX-300" }, failures: 2);

        await PublishAsync(new DeviceEventMessage(deviceId, 18, DateTimeOffset.UtcNow));

        var twin = await WaitForTwinAsync(deviceId, t => t.EventCount == 1);
        Assert.Equal("ap-south", twin.Region);
    }

    [Fact]
    public async Task Burst_of_events_for_many_devices_is_fully_processed()
    {
        const int devices = 5;
        const int eventsPerDevice = 4;
        var ids = Enumerable.Range(0, devices).Select(_ => NewDeviceId()).ToArray();
        foreach (var id in ids)
        {
            await app.WireMock.StubGetStatusAsync($"/devices/{id}", 404);
        }

        var start = DateTimeOffset.UtcNow;
        foreach (var id in ids)
        {
            var events = Enumerable.Range(0, eventsPerDevice)
                .Select(i => new DeviceEventMessage(id, 20 + i, start.AddSeconds(i)))
                .ToArray();
            await PublishAsync(events);
        }

        foreach (var id in ids)
        {
            var twin = await WaitForTwinAsync(id, t => t.EventCount == eventsPerDevice);
            Assert.Equal(20 + eventsPerDevice - 1, twin.LastTemperature);
            Assert.Equal("unknown", twin.Region);
        }
    }

    /// <summary>Partition key = device id, so one device's events stay ordered within a partition.</summary>
    private async Task PublishAsync(params DeviceEventMessage[] events)
    {
        using var batch = await app.Events.CreateBatchAsync(new CreateBatchOptions { PartitionKey = events[0].DeviceId }, Ct);
        foreach (var e in events)
        {
            Assert.True(batch.TryAdd(new EventData(BinaryData.FromObjectAsJson(e, Json))), "Event batch is full.");
        }

        await app.Events.SendAsync(batch, Ct);
    }

    private async Task<TwinResponse> WaitForTwinAsync(string deviceId, Func<TwinResponse, bool> condition)
    {
        var twin = await Eventually.UntilAsync(
            async ct =>
            {
                using var response = await app.Api.GetAsync($"/api/twins/{deviceId}", ct);
                return response.StatusCode == HttpStatusCode.NotFound
                    ? null
                    : await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<TwinResponse>(Json, ct);
            },
            t => t is not null && condition(t),
            $"twin {deviceId} to reach the expected state",
            timeout: TimeSpan.FromSeconds(90),
            cancellationToken: Ct);
        return twin!;
    }

    private static string NewDeviceId() => $"dev-{Guid.NewGuid():N}";
}

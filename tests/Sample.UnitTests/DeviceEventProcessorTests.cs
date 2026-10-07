using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sample.Application;

namespace Sample.UnitTests;

public sealed class DeviceEventProcessorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Publishes_alert_when_temperature_is_above_threshold()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var processor = CreateProcessor(repository, publisher);

        await processor.ProcessAsync(new DeviceEvent("dev-1", 95, Now), TestContext.Current.CancellationToken);

        var alert = Assert.Single(publisher.Alerts);
        Assert.Equal("dev-1", alert.DeviceId);
        Assert.Equal(DeviceStatus.Alert, repository.Saved.Last().Status);
    }

    [Fact]
    public async Task Does_not_publish_alert_for_normal_temperature()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var processor = CreateProcessor(repository, publisher);

        await processor.ProcessAsync(new DeviceEvent("dev-1", 20, Now), TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Alerts);
        Assert.Single(repository.Saved);
    }

    [Fact]
    public async Task Retries_on_concurrency_conflict()
    {
        var repository = new FakeRepository { ConflictsToThrow = 2 };
        var processor = CreateProcessor(repository, new FakePublisher());

        var result = await processor.ProcessAsync(new DeviceEvent("dev-1", 20, Now), TestContext.Current.CancellationToken);

        Assert.Equal(3, repository.SaveCalls);
        Assert.Equal(1, result.Twin.EventCount);
    }

    private static DeviceEventProcessor CreateProcessor(FakeRepository repository, FakePublisher publisher) =>
        new(
            new FakeMetadataClient(),
            repository,
            publisher,
            Options.Create(new SampleOptions { TemperatureAlertThreshold = 80 }),
            TimeProvider.System,
            NullLogger<DeviceEventProcessor>.Instance);

    private sealed class FakeMetadataClient : IDeviceMetadataClient
    {
        public Task<DeviceMetadata> GetAsync(string deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceMetadata("eu-west", "TX-100"));
    }

    private sealed class FakeRepository : ITwinRepository
    {
        public int ConflictsToThrow { get; set; }
        public int SaveCalls { get; private set; }
        public List<DeviceTwin> Saved { get; } = [];

        public Task<(DeviceTwin Twin, string ETag)?> GetAsync(string deviceId, CancellationToken cancellationToken) =>
            Task.FromResult<(DeviceTwin, string)?>(Saved.Count == 0 ? null : (Saved[^1], "etag"));

        public Task SaveAsync(DeviceTwin twin, string? etag, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ConflictsToThrow-- > 0)
            {
                throw new ConcurrencyConflictException(twin.DeviceId);
            }

            Saved.Add(twin);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher : IAlertPublisher
    {
        public List<DeviceAlert> Alerts { get; } = [];

        public Task PublishAsync(DeviceAlert alert, CancellationToken cancellationToken)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }
}

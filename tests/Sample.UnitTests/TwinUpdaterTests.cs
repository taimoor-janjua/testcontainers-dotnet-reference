namespace Sample.UnitTests;

public sealed class TwinUpdaterTests
{
    private const double Threshold = 80;
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceMetadata Metadata = new("eu-west", "TX-100");

    [Fact]
    public void First_event_creates_twin_with_metadata()
    {
        var result = TwinUpdater.Apply(null, Event(20, Now), Metadata, Threshold, Now);

        Assert.Equal("dev-1", result.Twin.Id);
        Assert.Equal("dev-1", result.Twin.DeviceId);
        Assert.Equal("eu-west", result.Twin.Region);
        Assert.Equal("TX-100", result.Twin.Model);
        Assert.Equal(20, result.Twin.LastTemperature);
        Assert.Equal(1, result.Twin.EventCount);
        Assert.Equal(DeviceStatus.Normal, result.Twin.Status);
        Assert.Null(result.Alert);
    }

    [Fact]
    public void Temperature_above_threshold_raises_alert()
    {
        var result = TwinUpdater.Apply(null, Event(95, Now), Metadata, Threshold, Now);

        Assert.Equal(DeviceStatus.Alert, result.Twin.Status);
        Assert.NotNull(result.Alert);
        Assert.Equal(95, result.Alert.Temperature);
        Assert.Equal(Threshold, result.Alert.Threshold);
    }

    [Fact]
    public void Temperature_equal_to_threshold_is_not_an_alert()
    {
        var result = TwinUpdater.Apply(null, Event(Threshold, Now), Metadata, Threshold, Now);

        Assert.Equal(DeviceStatus.Normal, result.Twin.Status);
        Assert.Null(result.Alert);
    }

    [Fact]
    public void Staying_in_alert_does_not_raise_a_second_alert()
    {
        var first = TwinUpdater.Apply(null, Event(95, Now), Metadata, Threshold, Now);
        var second = TwinUpdater.Apply(first.Twin, Event(99, Now.AddSeconds(1)), Metadata, Threshold, Now);

        Assert.Equal(DeviceStatus.Alert, second.Twin.Status);
        Assert.Null(second.Alert);
    }

    [Fact]
    public void Recovering_and_exceeding_again_raises_a_new_alert()
    {
        var twin = TwinUpdater.Apply(null, Event(95, Now), Metadata, Threshold, Now).Twin;
        twin = TwinUpdater.Apply(twin, Event(50, Now.AddSeconds(1)), Metadata, Threshold, Now).Twin;
        var result = TwinUpdater.Apply(twin, Event(90, Now.AddSeconds(2)), Metadata, Threshold, Now);

        Assert.NotNull(result.Alert);
    }

    [Fact]
    public void Out_of_order_event_is_counted_but_does_not_overwrite_newer_state()
    {
        var twin = TwinUpdater.Apply(null, Event(30, Now), Metadata, Threshold, Now).Twin;
        var result = TwinUpdater.Apply(twin, Event(99, Now.AddMinutes(-5)), Metadata, Threshold, Now);

        Assert.Equal(2, result.Twin.EventCount);
        Assert.Equal(30, result.Twin.LastTemperature);
        Assert.Equal(Now, result.Twin.LastEventAt);
        Assert.Equal(DeviceStatus.Normal, result.Twin.Status);
        Assert.Null(result.Alert);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_device_id_is_rejected(string deviceId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            TwinUpdater.Apply(null, new DeviceEvent(deviceId, 20, Now), Metadata, Threshold, Now));
    }

    private static DeviceEvent Event(double temperature, DateTimeOffset timestamp) => new("dev-1", temperature, timestamp);
}

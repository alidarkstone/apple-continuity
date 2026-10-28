using AirPodsWin.Core;
using Xunit;

namespace AirPodsWin.Core.Tests;

public class ProximityPairingMessageTests
{
    // AirPods Pro 2, left pod broadcasting, L 80 % R 90 % case 50 %, case charging, lid open (counter 2).
    static readonly byte[] ProTwoLidOpen =
    {
        0x07, 0x19, 0x01, 0x14, 0x20, 0x20, 0x98, 0x45, 0x02, 0x00,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    [Fact]
    public void ParsesModelBatteriesAndLid()
    {
        var m = ProximityPairingMessage.TryParse(ProTwoLidOpen)!;

        Assert.Equal(AirPodsModel.AirPodsPro2, m.Model);
        Assert.Equal(80, m.LeftBattery);
        Assert.Equal(90, m.RightBattery);
        Assert.Equal(50, m.CaseBattery);
        Assert.True(m.CaseCharging);
        Assert.False(m.LeftCharging);
        Assert.True(m.LidOpen);
        Assert.Equal(2, m.LidCounter);
        Assert.False(m.AnyInEar);
    }

    [Fact]
    public void SwapsSidesWhenRightPodBroadcasts()
    {
        var data = (byte[])ProTwoLidOpen.Clone();
        data[5] = 0x02;      // right pod broadcasting and in ear
        data[7] = 0x1F;      // right charging flag (bit 0 when right broadcasts), case unknown

        var m = ProximityPairingMessage.TryParse(data)!;

        Assert.Equal(90, m.LeftBattery);
        Assert.Equal(80, m.RightBattery);
        Assert.Null(m.CaseBattery);
        Assert.True(m.RightInEar);
        Assert.False(m.LeftInEar);
        Assert.True(m.RightCharging);
    }

    [Fact]
    public void RejectsOtherContinuityMessages()
    {
        var nearby = new byte[] { 0x10, 0x05, 0x01, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.Null(ProximityPairingMessage.TryParse(nearby));
        Assert.Null(ProximityPairingMessage.TryParse(new byte[] { 0x07, 0x19 }));
    }

    [Fact]
    public void UnknownModelStillParses()
    {
        var data = (byte[])ProTwoLidOpen.Clone();
        data[3] = 0x77;
        var m = ProximityPairingMessage.TryParse(data)!;
        Assert.Equal(AirPodsModel.Unknown, m.Model);
        Assert.Equal("AirPods", m.Model.DisplayName());
    }
}

public class AirPodsTrackerTests
{
    static ProximityPairingMessage Msg(bool lidOpen = true, byte counter = 1, bool inEar = false, int left = 80) => new()
    {
        Model = AirPodsModel.AirPodsPro2,
        LeftBattery = left,
        RightBattery = 80,
        LidOpen = lidOpen,
        LidCounter = counter,
        LeftInEar = inEar,
    };

    [Fact]
    public void LidOpenTriggersPopupOnceWithinCooldown()
    {
        var now = DateTimeOffset.UnixEpoch;
        var t = new AirPodsTracker(clock: () => now);

        Assert.Equal(TrackerEvent.LidOpened, t.Process(Msg(), -50));
        now += TimeSpan.FromSeconds(1);
        Assert.Equal(TrackerEvent.None, t.Process(Msg(), -50));

        now += TimeSpan.FromSeconds(1);
        t.Process(Msg(lidOpen: false), -50);
        now += TimeSpan.FromSeconds(1);
        Assert.NotEqual(TrackerEvent.LidOpened, t.Process(Msg(counter: 2), -50));

        now += TimeSpan.FromSeconds(30);
        t.Process(Msg(lidOpen: false), -50);
        Assert.Equal(TrackerEvent.LidOpened, t.Process(Msg(counter: 3), -50));
    }

    [Fact]
    public void IgnoresDistantDevices()
    {
        var t = new AirPodsTracker();
        Assert.Equal(TrackerEvent.None, t.Process(Msg(), -85));
        Assert.Null(t.Current);
    }

    [Fact]
    public void ReportsInEarTransitions()
    {
        var now = DateTimeOffset.UnixEpoch;
        var t = new AirPodsTracker(clock: () => now);
        t.Process(Msg(lidOpen: false), -50);
        Assert.Equal(TrackerEvent.PutInEar, t.Process(Msg(lidOpen: false, inEar: true), -50));
        Assert.Equal(TrackerEvent.RemovedFromEars, t.Process(Msg(lidOpen: false), -50));
    }

    [Fact]
    public void ModelFilterDropsOtherModels()
    {
        var t = new AirPodsTracker(new TrackerOptions { ModelFilter = AirPodsModel.AirPodsMax });
        Assert.Equal(TrackerEvent.None, t.Process(Msg(), -40));
    }
}

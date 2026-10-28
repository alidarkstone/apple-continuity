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

public class AppleDeviceTests
{
    // Real ids from a user's log: their renamed AirPods Pro 3, a Samsung TV and a Samsung monitor.
    const string AirPods = "Bluetooth#Bluetoothd8:b3:2f:f7:37:32-54:2a:43:73:a5:f0";
    const string Tv = "Bluetooth#Bluetoothd8:b3:2f:f7:37:32-b0:e4:5c:f8:8d:09";
    const string Monitor = "Bluetooth#Bluetoothd8:b3:2f:f7:37:32-1c:af:4a:27:2f:cf";

    [Fact]
    public void RecognisesAppleByAddressNotName()
    {
        Assert.True(AppleDevice.IsAppleDeviceId(AirPods));
        Assert.False(AppleDevice.IsAppleDeviceId(Tv));
        Assert.False(AppleDevice.IsAppleDeviceId(Monitor));
        Assert.False(AppleDevice.IsAppleDeviceId("garbage"));
    }

    [Fact]
    public void ParsesRemoteAddress()
    {
        Assert.True(AppleDevice.TryGetAddress(AirPods, out ulong address));
        Assert.Equal(0x542A4373A5F0UL, address);
        Assert.Equal("542A4373A5F0", AppleDevice.AddressHex(address));
    }

    [Fact]
    public void ParsesAddressFromPnpInstanceId()
    {
        const string instance = @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2024\7&1A2B3C4D&0&542A4373A5F0_C00000000";
        Assert.True(AppleDevice.TryGetAddressFromInstanceId(instance, out ulong address));
        Assert.Equal(0x542A4373A5F0UL, address);
    }

    [Fact]
    public void ReadsAppleProductIdFromHardwareIds()
    {
        var apple = new[] { @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&2024", @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_LOCALMFG&0002" };
        var samsung = new[] { @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&00010075_PID&0100" };

        Assert.Equal((ushort)0x2024, AppleDevice.AppleProductId(apple));
        Assert.Null(AppleDevice.AppleProductId(samsung));
        Assert.Null(AppleDevice.AppleProductId(Array.Empty<string>()));
    }
}

public class NearbyInfoTests
{
    [Fact]
    public void FindsNearbyInfoAmongOtherContinuityMessages()
    {
        // Handoff (0x0C, 2 bytes) followed by Nearby Info (0x10, 5 bytes) with "home screen" (0x1B).
        var data = new byte[] { 0x0C, 0x02, 0xAA, 0xBB, 0x10, 0x05, 0x1B, 0x1C, 0x01, 0x02, 0x03 };
        var m = NearbyInfoMessage.TryParse(data)!;
        Assert.True(m.IsInUse);
        Assert.False(m.IsInCall);

        Assert.True(NearbyInfoMessage.TryParse(new byte[] { 0x10, 0x05, 0x4E, 0, 0, 0, 0 })!.IsInCall);
        Assert.False(NearbyInfoMessage.TryParse(new byte[] { 0x10, 0x05, 0x07, 0, 0, 0, 0 })!.IsInUse); // lock screen
        Assert.Null(NearbyInfoMessage.TryParse(new byte[] { 0x07, 0x19, 0x01 }));
    }

    [Fact]
    public void TriggersWhenPhonePickedUpAfterIdle()
    {
        var now = DateTimeOffset.UnixEpoch;
        var t = new IPhoneActivityTracker(() => now);
        var inUse = new NearbyInfoMessage(0x0B);

        Assert.True(t.Process(inUse, -45));
        now += TimeSpan.FromSeconds(1);
        Assert.False(t.Process(inUse, -45));      // still in use, no new trigger

        now += TimeSpan.FromSeconds(90);
        Assert.True(t.Process(inUse, -45));       // idle long enough and past cooldown
        now += TimeSpan.FromSeconds(90);
        Assert.False(t.Process(inUse, -80));      // too far away
        Assert.False(t.Process(new NearbyInfoMessage(0x07), -40)); // locked
    }
}

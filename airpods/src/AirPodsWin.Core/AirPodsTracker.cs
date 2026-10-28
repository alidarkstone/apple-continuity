namespace AirPodsWin.Core;

public enum TrackerEvent
{
    None,
    /// <summary>Case lid opened near the PC: show the popup card.</summary>
    LidOpened,
    /// <summary>A pod went into an ear: candidate for automatic connection.</summary>
    PutInEar,
    /// <summary>Both pods left the ears.</summary>
    RemovedFromEars,
    /// <summary>Battery or charging values changed.</summary>
    StatusChanged,
}

public sealed record TrackerOptions
{
    /// <summary>Signals weaker than this are ignored (someone else's AirPods across the room).</summary>
    public short MinRssi { get; init; } = -62;
    /// <summary>Minimum time between two popups.</summary>
    public TimeSpan PopupCooldown { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>After this long without an advertisement the device counts as gone.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
    /// <summary>Only react to this model when set (taken from the paired device).</summary>
    public AirPodsModel? ModelFilter { get; init; }
}

/// <summary>
/// Turns a stream of advertisements into high level events. AirPods rotate
/// their BLE address, so the tracker follows "the strongest nearby AirPods"
/// rather than a fixed address.
/// </summary>
public sealed class AirPodsTracker
{
    readonly TrackerOptions _options;
    readonly Func<DateTimeOffset> _clock;
    DateTimeOffset _lastSeen = DateTimeOffset.MinValue;
    DateTimeOffset _lastPopup = DateTimeOffset.MinValue;

    public AirPodsTracker(TrackerOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _options = options ?? new TrackerOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ProximityPairingMessage? Current { get; private set; }
    public short CurrentRssi { get; private set; }

    public bool IsPresent => Current is not null && _clock() - _lastSeen < _options.Timeout;

    public TrackerEvent Process(ProximityPairingMessage message, short rssi)
    {
        if (rssi < _options.MinRssi)
            return TrackerEvent.None;
        if (_options.ModelFilter is { } filter && message.Model != filter)
            return TrackerEvent.None;

        var now = _clock();
        var previous = IsPresent ? Current : null;
        Current = message;
        CurrentRssi = rssi;
        _lastSeen = now;

        bool lidJustOpened = message.LidOpen && !message.AnyInEar &&
            (previous is null || !previous.LidOpen || previous.LidCounter != message.LidCounter);
        if (lidJustOpened && now - _lastPopup >= _options.PopupCooldown)
        {
            _lastPopup = now;
            return TrackerEvent.LidOpened;
        }

        if (message.AnyInEar && previous is { AnyInEar: false })
            return TrackerEvent.PutInEar;
        if (!message.AnyInEar && previous is { AnyInEar: true })
            return TrackerEvent.RemovedFromEars;

        if (previous is null || !SameStatus(previous, message))
            return TrackerEvent.StatusChanged;

        return TrackerEvent.None;
    }

    static bool SameStatus(ProximityPairingMessage a, ProximityPairingMessage b) =>
        a.LeftBattery == b.LeftBattery && a.RightBattery == b.RightBattery && a.CaseBattery == b.CaseBattery &&
        a.LeftCharging == b.LeftCharging && a.RightCharging == b.RightCharging && a.CaseCharging == b.CaseCharging &&
        a.LidOpen == b.LidOpen;
}

namespace AirPodsWin.Core;

/// <summary>
/// Apple Continuity "Nearby Info" message (type 0x10) that iPhones broadcast all the time.
/// Its first byte holds status flags (high nibble) and an activity code (low nibble) that
/// tells whether the screen is off, locked, on the home screen, or a call is ringing.
/// One advertisement can carry several Continuity messages back to back (type, length, payload).
/// </summary>
public sealed record NearbyInfoMessage(byte StatusAndAction)
{
    public const byte MessageType = 0x10;

    public int ActionCode => StatusAndAction & 0x0F;

    /// <summary>Unlocked and in use (home screen / an app open).</summary>
    public bool IsInUse => ActionCode == 0x0B;

    /// <summary>A phone or FaceTime call is ringing or active.</summary>
    public bool IsInCall => ActionCode == 0x0E;

    public static NearbyInfoMessage? TryParse(ReadOnlySpan<byte> manufacturerData)
    {
        int i = 0;
        while (i + 2 <= manufacturerData.Length)
        {
            byte type = manufacturerData[i];
            int length = manufacturerData[i + 1];
            if (i + 2 + length > manufacturerData.Length)
                return null;
            if (type == MessageType && length >= 1)
                return new NearbyInfoMessage(manufacturerData[i + 2]);
            i += 2 + length;
        }
        return null;
    }
}

/// <summary>
/// Decides when "the iPhone was just picked up" (or rang) close to the PC, so the PC can let the
/// AirPods go back to it. iPhones rotate their address, so this follows the strongest nearby
/// signal instead of one device, and needs the phone to be very close to avoid reacting to
/// someone else's iPhone across the room.
/// </summary>
public sealed class IPhoneActivityTracker
{
    readonly Func<DateTimeOffset> _clock;
    DateTimeOffset _lastActive = DateTimeOffset.MinValue;
    DateTimeOffset _lastTrigger = DateTimeOffset.MinValue;

    public IPhoneActivityTracker(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public short MinRssi { get; init; } = -55;
    /// <summary>The phone must have been idle/locked this long before "in use" counts as being picked up.</summary>
    public TimeSpan IdleBefore { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Returns true once when a close iPhone goes from idle to in use, or a call starts.</summary>
    public bool Process(NearbyInfoMessage message, short rssi)
    {
        if (rssi < MinRssi || !(message.IsInUse || message.IsInCall))
            return false;

        var now = _clock();
        bool wasIdle = now - _lastActive >= IdleBefore;
        _lastActive = now;

        if ((wasIdle || message.IsInCall) && now - _lastTrigger >= Cooldown)
        {
            _lastTrigger = now;
            return true;
        }
        return false;
    }
}

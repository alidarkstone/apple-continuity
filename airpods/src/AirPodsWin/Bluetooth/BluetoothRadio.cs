using Windows.Devices.Radios;

namespace AirPodsWin.Bluetooth;

/// <summary>
/// Turns the PC's Bluetooth radio on at startup and, on exit, back off only if
/// this app was the one that turned it on (so a Bluetooth mouse or keyboard the
/// user had running keeps working).
/// </summary>
public sealed class BluetoothRadio
{
    Radio? _radio;

    public bool TurnedOnByApp { get; private set; }
    public bool IsOn => _radio?.State == RadioState.On;

    /// <summary>Raised (on a background thread) when the radio is switched on or off.</summary>
    public event Action<bool>? StateChanged;

    public async Task<bool> EnsureOnAsync()
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            return false;

        if (_radio is null)
        {
            _radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_radio is null)
                return false;
            _radio.StateChanged += (r, _) => StateChanged?.Invoke(r.State == RadioState.On);
        }

        if (_radio.State == RadioState.On)
            return true;

        // RadioState.Disabled means the adapter itself is disabled in Device Manager; that can't be changed from here.
        if (_radio.State == RadioState.Disabled)
            return false;

        bool ok = await _radio.SetStateAsync(RadioState.On) == RadioAccessStatus.Allowed;
        TurnedOnByApp |= ok;
        return ok;
    }

    /// <summary>Called on exit: switch the radio off again if the app switched it on.</summary>
    public void Restore()
    {
        if (!TurnedOnByApp || _radio is null || _radio.State != RadioState.On)
            return;
        _radio.SetStateAsync(RadioState.Off).AsTask().Wait(TimeSpan.FromSeconds(3));
    }
}

using AirPodsWin.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsWin.Bluetooth;

public sealed record PairingCandidate(string Id, string Name, bool IsAppleAudio, bool IsPresent);

/// <summary>
/// Pairs AirPods with Windows without going through Settings. While running it lists
/// nearby unpaired audio devices; the app pairs the one that is an Apple audio device
/// (by its Bluetooth address, see <see cref="AppleDevice"/>, never by name), or lets the
/// user pick from the list. It never picks an arbitrary device on its own.
/// The case must be in pairing mode (light flashing white) for pairing to succeed.
/// </summary>
public sealed class AirPodsPairing : IDisposable
{
    const string CodMajorProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
    const string IsPresentProperty = "System.Devices.Aep.IsPresent";
    const int CodMajorAudioVideo = 4;

    readonly DeviceWatcher _watcher;
    readonly Dictionary<string, DeviceInformation> _devices = new();

    /// <summary>Raised (on a background thread) whenever the candidate list changes.</summary>
    public event Action? CandidatesChanged;

    public AirPodsPairing()
    {
        _watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            new[] { CodMajorProperty, IsPresentProperty },
            DeviceInformationKind.AssociationEndpoint);

        _watcher.Added += (_, info) =>
        {
            lock (_devices)
                _devices[info.Id] = info;
            Log.Write($"Pairing scan: '{info.Name}' audio={IsAudio(info)} apple={AppleDevice.IsAppleDeviceId(info.Id)} present={IsPresent(info)}");
            CandidatesChanged?.Invoke();
        };
        _watcher.Updated += (_, update) =>
        {
            lock (_devices)
            {
                if (!_devices.TryGetValue(update.Id, out var info))
                    return;
                info.Update(update);
            }
            CandidatesChanged?.Invoke();
        };
        _watcher.Removed += (_, update) =>
        {
            lock (_devices)
                _devices.Remove(update.Id);
            CandidatesChanged?.Invoke();
        };
    }

    public void Start() => _watcher.Start();

    /// <summary>Named audio devices, Apple and present ones first.</summary>
    public IReadOnlyList<PairingCandidate> Candidates
    {
        get
        {
            lock (_devices)
            {
                return _devices.Values
                    .Where(d => d.Name.Length > 0 && IsAudio(d))
                    .Select(d => new PairingCandidate(d.Id, d.Name, AppleDevice.IsAppleDeviceId(d.Id), IsPresent(d)))
                    .OrderByDescending(c => c.IsAppleAudio)
                    .ThenByDescending(c => c.IsPresent)
                    .ThenBy(c => c.Name)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Tries to pair the given device until it succeeds or the deadline passes; the first
    /// attempts usually fail while the case is not yet in pairing mode.
    /// </summary>
    public async Task<PairedDevice?> PairAsync(string id, DateTime deadline, CancellationToken cancel)
    {
        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            DeviceInformation? device;
            lock (_devices)
                _devices.TryGetValue(id, out device);
            device ??= await DeviceInformation.CreateFromIdAsync(id);

            var custom = device.Pairing.Custom;
            custom.PairingRequested += (_, args) => args.Accept();
            var result = await custom.PairAsync(
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                DevicePairingProtectionLevel.Default);
            Log.Write($"Pairing '{device.Name}': {result.Status}");

            if (result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
                return new PairedDevice(device.Id, device.Name, false);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancel);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        return null;
    }

    static bool IsAudio(DeviceInformation info) =>
        info.Properties.TryGetValue(CodMajorProperty, out var major) && major is not null &&
        Convert.ToInt32(major) == CodMajorAudioVideo;

    static bool IsPresent(DeviceInformation info) =>
        info.Properties.TryGetValue(IsPresentProperty, out var present) && present is true;

    public void Dispose()
    {
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
    }
}

using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsWin.Bluetooth;

/// <summary>
/// Pairs AirPods with Windows without going through Settings: discovers a nearby
/// unpaired classic Bluetooth device named like AirPods/Beats (the case must be in
/// pairing mode, i.e. its back button held until the light flashes white) and
/// accepts the "just works" pairing.
/// </summary>
public static class AirPodsPairing
{
    static readonly string[] NameHints = { "AirPods", "Beats", "Powerbeats" };
    const string CodMajorProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
    const int CodMajorAudioVideo = 4;

    public static async Task<PairedDevice?> PairAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        var found = new TaskCompletionSource<DeviceInformation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new Dictionary<string, DeviceInformation>();

        var watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            new[] { CodMajorProperty },
            DeviceInformationKind.AssociationEndpoint);

        // AirPods renamed on the iPhone ("Kulaklığım") don't contain "AirPods", so also accept
        // any headset (Bluetooth class Audio/Video) that is in pairing mode right now.
        void Check(DeviceInformation info)
        {
            bool nameMatch = NameHints.Any(h => info.Name.Contains(h, StringComparison.OrdinalIgnoreCase));
            bool isAudio = info.Properties.TryGetValue(CodMajorProperty, out var major) &&
                           major is not null && Convert.ToInt32(major) == CodMajorAudioVideo;
            Log.Write($"Pairing scan: '{info.Name}' audio={isAudio} id={info.Id}");
            if (nameMatch || (isAudio && info.Name.Length > 0))
                found.TrySetResult(info);
        }

        watcher.Added += (_, info) =>
        {
            lock (seen)
                seen[info.Id] = info;
            Check(info);
        };
        watcher.Updated += (_, update) =>
        {
            DeviceInformation? info;
            lock (seen)
            {
                if (!seen.TryGetValue(update.Id, out info))
                    return;
            }
            info.Update(update);
            Check(info);
        };

        watcher.Start();
        try
        {
            using var timer = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancel);
            using (linked.Token.Register(() => found.TrySetCanceled()))
            {
                DeviceInformation device;
                try
                {
                    device = await found.Task;
                }
                catch (TaskCanceledException)
                {
                    Log.Write("Pairing: no AirPods in pairing mode found before timeout");
                    return null;
                }

                var custom = device.Pairing.Custom;
                custom.PairingRequested += (_, args) => args.Accept();
                var result = await custom.PairAsync(
                    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.Default);

                Log.Write($"Pairing '{device.Name}': {result.Status}");
                bool ok = result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
                return ok ? new PairedDevice(device.Id, device.Name, false) : null;
            }
        }
        finally
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
    }
}

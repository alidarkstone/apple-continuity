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

    public static async Task<PairedDevice?> PairAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        var found = new TaskCompletionSource<DeviceInformation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new Dictionary<string, DeviceInformation>();

        var watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            null,
            DeviceInformationKind.AssociationEndpoint);

        void Check(DeviceInformation info)
        {
            if (NameHints.Any(h => info.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))
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
                    return null;
                }

                var custom = device.Pairing.Custom;
                custom.PairingRequested += (_, args) => args.Accept();
                var result = await custom.PairAsync(
                    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.Default);

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

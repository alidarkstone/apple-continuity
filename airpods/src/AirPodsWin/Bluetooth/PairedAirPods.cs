using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsWin.Bluetooth;

public sealed record PairedDevice(string Id, string Name, bool IsConnected);

/// <summary>
/// Finds AirPods / Beats headsets that are already paired with Windows (classic Bluetooth).
/// Pairing itself is done once through Windows Settings.
/// </summary>
public static class PairedAirPods
{
    static readonly string[] NameHints = { "AirPods", "Beats", "Powerbeats" };

    public static async Task<IReadOnlyList<PairedDevice>> FindAsync(string? knownName = null)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector);
        var result = new List<(int Rank, PairedDevice Device)>();

        foreach (var info in infos)
        {
            bool known = !string.IsNullOrEmpty(knownName) && info.Name.Equals(knownName, StringComparison.OrdinalIgnoreCase);
            bool hinted = NameHints.Any(h => info.Name.Contains(h, StringComparison.OrdinalIgnoreCase));

            bool connected = false;
            bool headset = false;
            try
            {
                using var device = await BluetoothDevice.FromIdAsync(info.Id);
                connected = device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
                headset = device?.ClassOfDevice.MajorClass == BluetoothMajorClass.AudioVideo;
            }
            catch
            {
                // Device may be transiently unavailable; treat as disconnected.
            }

            // AirPods renamed on the iPhone ("Kulaklığım") only show up as a headset; rank them last.
            int rank = known ? 0 : hinted ? 1 : headset ? 2 : -1;
            if (rank >= 0)
                result.Add((rank, new PairedDevice(info.Id, info.Name, connected)));
        }
        return result.OrderBy(r => r.Rank).Select(r => r.Device).ToList();
    }

    public static async Task<bool> IsConnectedAsync(string deviceId)
    {
        using var device = await BluetoothDevice.FromIdAsync(deviceId);
        return device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }
}

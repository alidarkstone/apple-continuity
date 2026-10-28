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

    public static async Task<IReadOnlyList<PairedDevice>> FindAsync()
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector);
        var result = new List<PairedDevice>();

        foreach (var info in infos)
        {
            if (!NameHints.Any(h => info.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                continue;

            bool connected = false;
            try
            {
                using var device = await BluetoothDevice.FromIdAsync(info.Id);
                connected = device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            }
            catch
            {
                // Device may be transiently unavailable; treat as disconnected.
            }
            result.Add(new PairedDevice(info.Id, info.Name, connected));
        }
        return result;
    }

    public static async Task<bool> IsConnectedAsync(string deviceId)
    {
        using var device = await BluetoothDevice.FromIdAsync(deviceId);
        return device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }
}

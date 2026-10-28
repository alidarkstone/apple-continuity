using AirPodsWin.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsWin.Bluetooth;

public sealed record PairedDevice(string Id, string Name, bool IsConnected);

/// <summary>
/// Finds AirPods that are already paired with Windows (classic Bluetooth): the one the user
/// chose (by id) first, then Apple audio devices. "Apple" comes from the Bluetooth Device ID
/// Windows recorded at pairing (VID 004C), or from the address' Apple OUI; the name is never
/// used, so renamed AirPods are found and a TV or speaker is never picked on its own.
/// </summary>
public static class PairedAirPods
{
    public static async Task<IReadOnlyList<PairedDevice>> FindAsync(string? knownId = null, bool includeAllAudio = false)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector);
        var appleProducts = await AppleProductIdsByAddressAsync();
        var result = new List<(int Rank, PairedDevice Device)>();

        foreach (var info in infos)
        {
            bool known = !string.IsNullOrEmpty(knownId) && info.Id == knownId;
            bool apple = AppleDevice.TryGetAddress(info.Id, out ulong address) &&
                         (appleProducts.ContainsKey(address) || AppleDevice.IsAppleAddress(address));

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

            int rank = known ? 0 : apple && headset ? 1 : includeAllAudio && headset ? 2 : -1;
            if (rank >= 0)
                result.Add((rank, new PairedDevice(info.Id, info.Name, connected)));
        }
        return result.OrderBy(r => r.Rank).Select(r => r.Device).ToList();
    }

    /// <summary>
    /// Bluetooth address -> Apple product id, from the PnP hardware ids Windows created for each
    /// paired device's services (<c>BTHENUM\{...}_VID&amp;0001004C_PID&amp;2024\...&amp;542A4373A5F0_C...</c>).
    /// </summary>
    static async Task<Dictionary<ulong, ushort>> AppleProductIdsByAddressAsync()
    {
        var result = new Dictionary<ulong, ushort>();
        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                "System.Devices.DeviceInstanceId:~<\"BTHENUM\"",
                new[] { "System.Devices.HardwareIds" },
                DeviceInformationKind.Device);

            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue("System.Devices.HardwareIds", out var ids) && ids is string[] hardwareIds &&
                    AppleDevice.AppleProductId(hardwareIds) is ushort product &&
                    AppleDevice.TryGetAddressFromInstanceId(node.Id, out ulong address))
                {
                    result[address] = product;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Device ID lookup failed, falling back to address check: " + ex.Message);
        }
        return result;
    }

    public static async Task<bool> IsConnectedAsync(string deviceId)
    {
        using var device = await BluetoothDevice.FromIdAsync(deviceId);
        return device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace AirPodsWin.Core;

/// <summary>
/// Identifies Apple Bluetooth devices from hard identifiers instead of their (user-editable) name:
/// <list type="bullet">
/// <item>the Bluetooth Device ID that Windows stores for paired devices in their PnP hardware ids,
/// e.g. <c>BTHENUM\{0000110b-...}_VID&amp;0001004c_PID&amp;2024</c> (source 0001 = Bluetooth SIG,
/// vendor 004C = Apple, product = model);</item>
/// <item>before pairing, when no Device ID is readable yet, the manufacturer part (OUI) of the
/// device's public Bluetooth address, checked against Apple's IEEE registrations.</item>
/// </list>
/// </summary>
public static partial class AppleDevice
{
    public const ushort AppleVendorId = 0x004C;

    static readonly Regex AddressInDeviceId = new(
        @"([0-9a-f]{2}(?::[0-9a-f]{2}){5})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex VendorProduct = new(
        @"VID&0001([0-9a-f]{4})_PID&([0-9a-f]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>True when the 48-bit address belongs to an OUI registered to Apple.</summary>
    public static bool IsAppleAddress(ulong address) =>
        Array.BinarySearch(AppleOuis, (uint)(address >> 24) & 0xFFFFFF) >= 0;

    /// <summary>
    /// Reads the remote address from a Windows Bluetooth device id such as
    /// <c>Bluetooth#Bluetoothd8:b3:2f:f7:37:32-54:2a:43:73:a5:f0</c> (local-remote).
    /// </summary>
    public static bool TryGetAddress(string deviceId, out ulong address)
    {
        address = 0;
        var match = AddressInDeviceId.Match(deviceId);
        return match.Success &&
               ulong.TryParse(match.Groups[1].Value.Replace(":", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    public static bool IsAppleDeviceId(string deviceId) =>
        TryGetAddress(deviceId, out ulong address) && IsAppleAddress(address);

    /// <summary>
    /// Finds the Bluetooth SIG vendor/product pair in PnP hardware ids. Returns the Apple product id
    /// (the same model id as in the BLE advertisement, e.g. 0x2024) or null when not an Apple device.
    /// </summary>
    public static ushort? AppleProductId(IEnumerable<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            var match = VendorProduct.Match(id);
            if (match.Success && ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber) == AppleVendorId)
                return ushort.Parse(match.Groups[2].Value, NumberStyles.HexNumber);
        }
        return null;
    }

    static readonly Regex AddressInInstanceId = new(
        @"([0-9a-f]{12})_C[0-9a-f]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the device address from a Bluetooth PnP instance id such as
    /// <c>BTHENUM\{0000110b-...}_VID&amp;0001004c_PID&amp;2024\7&amp;1a2b3c&amp;0&amp;542A4373A5F0_C00000000</c>.
    /// </summary>
    public static bool TryGetAddressFromInstanceId(string instanceId, out ulong address)
    {
        address = 0;
        var match = AddressInInstanceId.Match(instanceId);
        return match.Success &&
               ulong.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>"542A4373A5F0" form used inside PnP instance ids.</summary>
    public static string AddressHex(ulong address) => address.ToString("X12", CultureInfo.InvariantCulture);
}

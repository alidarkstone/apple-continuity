namespace AirPodsWin.Core;

/// <summary>
/// Model identifiers carried in bytes 3-4 (little-endian) of the Apple
/// "proximity pairing" BLE advertisement.
/// </summary>
public enum AirPodsModel : ushort
{
    Unknown = 0,
    AirPods1 = 0x2002,
    AirPods2 = 0x200F,
    AirPods3 = 0x2013,
    AirPods4 = 0x2019,
    AirPods4Anc = 0x201B,
    AirPodsPro = 0x200E,
    AirPodsPro2 = 0x2014,
    AirPodsPro2UsbC = 0x2024,
    AirPodsMax = 0x200A,
    AirPodsMaxUsbC = 0x201F,
    PowerbeatsPro = 0x200B,
    BeatsFitPro = 0x2012,
    BeatsStudioBuds = 0x2011,
}

public static class AirPodsModelExtensions
{
    public static string DisplayName(this AirPodsModel model) => model switch
    {
        AirPodsModel.AirPods1 => "AirPods",
        AirPodsModel.AirPods2 => "AirPods (2. nesil)",
        AirPodsModel.AirPods3 => "AirPods (3. nesil)",
        AirPodsModel.AirPods4 => "AirPods 4",
        AirPodsModel.AirPods4Anc => "AirPods 4 (ANC)",
        AirPodsModel.AirPodsPro => "AirPods Pro",
        AirPodsModel.AirPodsPro2 => "AirPods Pro 2",
        AirPodsModel.AirPodsPro2UsbC => "AirPods Pro 2 (USB-C)",
        AirPodsModel.AirPodsMax => "AirPods Max",
        AirPodsModel.AirPodsMaxUsbC => "AirPods Max (USB-C)",
        AirPodsModel.PowerbeatsPro => "Powerbeats Pro",
        AirPodsModel.BeatsFitPro => "Beats Fit Pro",
        AirPodsModel.BeatsStudioBuds => "Beats Studio Buds",
        _ => "AirPods",
    };

    /// <summary>Over-ear models report a single battery and have no case.</summary>
    public static bool IsSingleBattery(this AirPodsModel model) =>
        model is AirPodsModel.AirPodsMax or AirPodsModel.AirPodsMaxUsbC;

    public static bool IsPro(this AirPodsModel model) =>
        model is AirPodsModel.AirPodsPro or AirPodsModel.AirPodsPro2 or AirPodsModel.AirPodsPro2UsbC;
}

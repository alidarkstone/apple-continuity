namespace IPhoneMirror.Core;

/// <summary>Line commands understood by the ESP32 controller firmware (see firmware/src/main.cpp).</summary>
public static class Protocol
{
    public const string Query = "?";

    // HID Consumer page usages
    public const int Home = 0x223;
    public const int VolumeUp = 0xE9;
    public const int VolumeDown = 0xEA;
    public const int PlayPause = 0xCD;

    public static string Repeat(int dx, int dy, int count) => $"r {dx} {dy} {count}";
    public static string Move(int dx, int dy) => $"m {dx} {dy}";
    public static string Buttons(int mask) => $"b {mask}";
    public static string Wheel(int amount) => $"w {Math.Clamp(amount, -127, 127)}";
    public static string Consumer(int usage) => $"c {usage}";
    public static string Interval(int ms) => $"i {ms}";

    public static string Keyboard(byte modifiers, IEnumerable<byte> keys)
    {
        var list = keys.Take(6).ToList();
        return list.Count == 0 ? $"k {modifiers}" : $"k {modifiers} {string.Join(' ', list)}";
    }

    /// <summary>Parses a status line from the controller: true = paired with the iPhone, false = waiting, null = not a status line.</summary>
    public static bool? ParseStatus(string line) => line.Trim() switch
    {
        "IPM bagli" => true,
        "IPM bekliyor" or "IPM hazir" => false,
        _ => null,
    };
}

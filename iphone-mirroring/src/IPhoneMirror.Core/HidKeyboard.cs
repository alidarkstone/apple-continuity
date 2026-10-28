namespace IPhoneMirror.Core;

/// <summary>
/// Turns Windows key events (set-1 scan codes, so key positions rather than characters)
/// into HID keyboard reports. The iPhone applies its own hardware-keyboard layout,
/// so it should be set to the same layout as the PC (e.g. Turkish Q).
/// </summary>
public sealed class HidKeyboard
{
    const byte LCtrl = 0x01, LShift = 0x02, LAlt = 0x04, LGui = 0x08;
    const byte RCtrl = 0x10, RShift = 0x20, RAlt = 0x40, RGui = 0x80;

    static readonly Dictionary<int, byte> Plain = new()
    {
        [0x01] = 0x29, [0x02] = 0x1E, [0x03] = 0x1F, [0x04] = 0x20, [0x05] = 0x21, [0x06] = 0x22,
        [0x07] = 0x23, [0x08] = 0x24, [0x09] = 0x25, [0x0A] = 0x26, [0x0B] = 0x27, [0x0C] = 0x2D,
        [0x0D] = 0x2E, [0x0E] = 0x2A, [0x0F] = 0x2B, [0x10] = 0x14, [0x11] = 0x1A, [0x12] = 0x08,
        [0x13] = 0x15, [0x14] = 0x17, [0x15] = 0x1C, [0x16] = 0x18, [0x17] = 0x0C, [0x18] = 0x12,
        [0x19] = 0x13, [0x1A] = 0x2F, [0x1B] = 0x30, [0x1C] = 0x28, [0x1E] = 0x04, [0x1F] = 0x16,
        [0x20] = 0x07, [0x21] = 0x09, [0x22] = 0x0A, [0x23] = 0x0B, [0x24] = 0x0D, [0x25] = 0x0E,
        [0x26] = 0x0F, [0x27] = 0x33, [0x28] = 0x34, [0x29] = 0x35, [0x2B] = 0x31, [0x2C] = 0x1D,
        [0x2D] = 0x1B, [0x2E] = 0x06, [0x2F] = 0x19, [0x30] = 0x05, [0x31] = 0x11, [0x32] = 0x10,
        [0x33] = 0x36, [0x34] = 0x37, [0x35] = 0x38, [0x37] = 0x55, [0x39] = 0x2C, [0x3A] = 0x39,
        [0x3B] = 0x3A, [0x3C] = 0x3B, [0x3D] = 0x3C, [0x3E] = 0x3D, [0x3F] = 0x3E, [0x40] = 0x3F,
        [0x41] = 0x40, [0x42] = 0x41, [0x43] = 0x42, [0x44] = 0x43, [0x45] = 0x53, [0x46] = 0x47,
        [0x47] = 0x5F, [0x48] = 0x60, [0x49] = 0x61, [0x4A] = 0x56, [0x4B] = 0x5C, [0x4C] = 0x5D,
        [0x4D] = 0x5E, [0x4E] = 0x57, [0x4F] = 0x59, [0x50] = 0x5A, [0x51] = 0x5B, [0x52] = 0x62,
        [0x53] = 0x63, [0x56] = 0x64, [0x57] = 0x44, [0x58] = 0x45,
    };

    static readonly Dictionary<int, byte> Extended = new()
    {
        [0x1C] = 0x58, [0x35] = 0x54, [0x47] = 0x4A, [0x48] = 0x52, [0x49] = 0x4B, [0x4B] = 0x50,
        [0x4D] = 0x4F, [0x4F] = 0x4D, [0x50] = 0x51, [0x51] = 0x4E, [0x52] = 0x49, [0x53] = 0x4C,
        [0x5D] = 0x65,
    };

    readonly List<byte> _keys = [];
    byte _mods;

    /// <summary>Ctrl acts as ⌘ (so Ctrl+C / Ctrl+V / Ctrl+Space behave like on a Mac), Windows key as Control.</summary>
    public bool CtrlAsCommand { get; set; } = true;

    public static (byte Usage, byte Modifier) Translate(int scanCode, bool extended, bool ctrlAsCommand)
    {
        byte mod = (scanCode, extended) switch
        {
            (0x1D, false) => ctrlAsCommand ? LGui : LCtrl,
            (0x1D, true) => ctrlAsCommand ? RGui : RCtrl,
            (0x2A, _) => LShift,
            (0x36, _) => RShift,
            (0x38, false) => LAlt,
            (0x38, true) => RAlt,
            (0x5B, true) => ctrlAsCommand ? LCtrl : LGui,
            (0x5C, true) => ctrlAsCommand ? RCtrl : RGui,
            _ => (byte)0,
        };
        if (mod != 0) return (0, mod);
        var table = extended ? Extended : Plain;
        return (table.TryGetValue(scanCode, out var usage) ? usage : (byte)0, 0);
    }

    /// <summary>Returns the new report command, or null when nothing changed (e.g. key auto-repeat).</summary>
    public string? KeyDown(int scanCode, bool extended)
    {
        var (usage, mod) = Translate(scanCode, extended, CtrlAsCommand);
        if (mod != 0)
        {
            if ((_mods & mod) == mod) return null;
            _mods |= mod;
            return Report();
        }
        if (usage == 0 || _keys.Contains(usage)) return null;
        if (_keys.Count == 6) _keys.RemoveAt(0);
        _keys.Add(usage);
        return Report();
    }

    public string? KeyUp(int scanCode, bool extended)
    {
        var (usage, mod) = Translate(scanCode, extended, CtrlAsCommand);
        if (mod != 0)
        {
            if ((_mods & mod) == 0) return null;
            _mods &= (byte)~mod;
            return Report();
        }
        return _keys.Remove(usage) ? Report() : null;
    }

    public string ReleaseAll()
    {
        _keys.Clear();
        _mods = 0;
        return Report();
    }

    string Report()
    {
        byte mods = _mods;
        // AltGr arrives from Windows as a fake left Ctrl + right Alt; on the iPhone it is just ⌥.
        if ((mods & RAlt) != 0) mods = (byte)(mods & ~(LCtrl | LGui));
        return Protocol.Keyboard(mods, _keys);
    }
}

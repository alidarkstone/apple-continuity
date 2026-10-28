using System.IO;
using System.Text.Json;
using IPhoneMirror.Core;

namespace IPhoneMirror;

public sealed class AppSettings
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iPhoneYansitma");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    /// <summary>Name the PC shows up as in the iPhone's Screen Mirroring list.</summary>
    public string ReceiverName { get; set; } = "Windows";
    /// <summary>Full path to uxplay.exe; empty = look next to the app, then in MSYS2.</summary>
    public string UxPlayPath { get; set; } = "";
    /// <summary>Controller COM port; empty = find it automatically.</summary>
    public string ComPort { get; set; } = "";
    public bool CtrlAsCommand { get; set; } = true;
    public PointerSettings Pointer { get; set; } = new();
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = 820;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            // A broken settings file should not keep the app from starting.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }
}

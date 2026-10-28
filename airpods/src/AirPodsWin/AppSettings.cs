using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace AirPodsWin;

public sealed class AppSettings
{
    /// <summary>Windows id of "my" paired AirPods, set when pairing from the card or choosing in the tray menu.</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>Show the card when the case lid is opened near the PC.</summary>
    public bool ShowPopupOnLidOpen { get; set; } = true;
    /// <summary>Connect straight away when the lid opens, without waiting for a click.</summary>
    public bool AutoConnectOnLidOpen { get; set; }
    /// <summary>Connect when a pod goes into an ear while the PC is nearby (macOS-like automatic switching).</summary>
    public bool AutoConnectOnInEar { get; set; } = true;
    /// <summary>Let the AirPods go back to the iPhone when it is picked up (or rings) next to the PC while the PC is silent.</summary>
    public bool ReleaseToIPhone { get; set; } = true;
    /// <summary>Turn Bluetooth on at startup, and off again on exit if the app turned it on.</summary>
    public bool ManageBluetooth { get; set; } = true;
    /// <summary>Signal threshold in dBm; raise toward -50 if someone else's AirPods trigger the popup.</summary>
    public short MinRssi { get; set; } = -62;

    static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirPodsWin", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "AirPodsWin";

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}

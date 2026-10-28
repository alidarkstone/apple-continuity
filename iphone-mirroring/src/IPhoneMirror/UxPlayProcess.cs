using System.Diagnostics;
using System.IO;

namespace IPhoneMirror;

/// <summary>Runs UxPlay, the open-source AirPlay mirroring receiver, and finds the video window it opens.</summary>
sealed class UxPlayProcess : IDisposable
{
    Process? _process;

    public string? LastError { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    public static string? Locate(string configured)
    {
        string[] candidates =
        [
            configured,
            Path.Combine(AppContext.BaseDirectory, "uxplay", "uxplay.exe"),
            @"C:\msys64\ucrt64\bin\uxplay.exe",
        ];
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }

    public bool Start(string exe, string receiverName)
    {
        KillLeftovers(exe);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        // -nh: no "@hostname" suffix; d3d11videosink: GPU decode/render, low CPU.
        foreach (var arg in new[] { "-n", receiverName, "-nh", "-vs", "d3d11videosink", "-fps", "60" })
            psi.ArgumentList.Add(arg);

        // The bundled build carries its own GStreamer; point GStreamer at it.
        string dir = Path.GetDirectoryName(exe)!;
        string plugins = Path.Combine(dir, "lib", "gstreamer-1.0");
        if (Directory.Exists(plugins))
        {
            psi.Environment["PATH"] = dir + ";" + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["GST_PLUGIN_PATH"] = plugins;
            psi.Environment["GST_PLUGIN_SYSTEM_PATH"] = plugins;
            psi.Environment["GST_REGISTRY"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iPhoneYansitma", "gst-registry.bin");
            string scanner = Path.Combine(dir, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
            if (File.Exists(scanner)) psi.Environment["GST_PLUGIN_SCANNER"] = scanner;
        }

        try
        {
            _process = Process.Start(psi);
            if (_process == null) return false;
            _process.OutputDataReceived += (_, e) => Remember(e.Data);
            _process.ErrorDataReceived += (_, e) => Remember(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    void Remember(string? line)
    {
        if (line != null && line.Contains("ERROR", StringComparison.Ordinal)) LastError = line.Trim('*', ' ');
    }

    /// <summary>The top-level video window UxPlay opened for the current connection, if any.</summary>
    public IntPtr FindVideoWindow()
    {
        if (!IsRunning) return IntPtr.Zero;
        uint pid = (uint)_process!.Id;
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == pid && Native.IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static void KillLeftovers(string exe)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch (Exception)
            {
                // Process of another user or already gone.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (IsRunning) _process!.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
        _process?.Dispose();
        _process = null;
    }
}

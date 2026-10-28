using System.Runtime.InteropServices;

namespace AirPodsWin.Audio;

/// <summary>Reports whether the PC is currently playing sound on its default output.</summary>
public static class AudioActivity
{
    const uint ClsCtxAll = 0x17;
    const float SilenceThreshold = 0.001f;

    /// <summary>Peak level of the default render endpoint right now; false on any error.</summary>
    public static bool IsPlaying()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? meterObj = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            var iid = typeof(IAudioMeterInformation).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out meterObj);
            ((IAudioMeterInformation)meterObj).GetPeakValue(out float peak);
            return peak > SilenceThreshold;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            foreach (var o in new[] { meterObj, device, enumerator })
                if (o is not null && Marshal.IsComObject(o))
                    Marshal.ReleaseComObject(o);
        }
    }
}

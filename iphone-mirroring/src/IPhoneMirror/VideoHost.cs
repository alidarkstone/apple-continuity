using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IPhoneMirror;

/// <summary>
/// A black Win32 child area inside the WPF window. UxPlay's video window (a separate
/// process) is re-parented into it so the mirror looks like part of this app.
/// </summary>
sealed class VideoHost : HwndHost
{
    IntPtr _host, _video;

    public bool HasVideo => _video != IntPtr.Zero && Native.IsWindow(_video);

    /// <summary>Client size the video window had before it was embedded (= the stream's frame size, scaled).</summary>
    public event Action<int, int>? VideoAttached;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _host = Native.CreateWindowEx(0, "static", "",
            (int)(Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN) | Native.SS_BLACKRECT,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _host);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (HasVideo) Native.SetParent(_video, IntPtr.Zero);
        Native.DestroyWindow(hwnd.Handle);
    }

    public void Attach(IntPtr video)
    {
        if (_host == IntPtr.Zero || video == _video) return;

        Native.GetClientRect(video, out var r);

        long style = Native.GetWindowLongPtr(video, Native.GWL_STYLE).ToInt64();
        style &= ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU |
                   Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
        style |= Native.WS_CHILD | Native.WS_VISIBLE;
        Native.SetWindowLongPtr(video, Native.GWL_STYLE, new IntPtr(style));

        long ex = Native.GetWindowLongPtr(video, Native.GWL_EXSTYLE).ToInt64();
        ex &= ~(Native.WS_EX_APPWINDOW | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE | Native.WS_EX_DLGMODALFRAME);
        Native.SetWindowLongPtr(video, Native.GWL_EXSTYLE, new IntPtr(ex));

        Native.SetParent(video, _host);
        _video = video;
        FitVideo();

        if (r.Right > 0 && r.Bottom > 0) VideoAttached?.Invoke(r.Right, r.Bottom);
    }

    /// <summary>Forget a video window that UxPlay closed (it opens a new one for the next connection).</summary>
    public void DropIfGone()
    {
        if (_video != IntPtr.Zero && !Native.IsWindow(_video)) _video = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        FitVideo();
    }

    void FitVideo()
    {
        if (!HasVideo) return;
        Native.GetClientRect(_host, out var r);
        Native.MoveWindow(_video, 0, 0, r.Right, r.Bottom, true);
    }
}

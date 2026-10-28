using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using IPhoneMirror.Core;

namespace IPhoneMirror;

public partial class MainWindow : Window
{
    static readonly TimeSpan DragSendInterval = TimeSpan.FromMilliseconds(15);

    readonly AppSettings _settings = AppSettings.Load();
    readonly UxPlayProcess _uxplay = new();
    readonly ControllerLink _controller = new();
    readonly InputOverlay _overlay = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly PointerPlanner _pointer;
    readonly HidKeyboard _keyboard = new();

    string? _uxplayExe;
    DateTime _lastStart = DateTime.MinValue, _lastControllerTry = DateTime.MinValue;
    bool _connecting;
    double _videoW = 9, _videoH = 19.5;
    bool _calibrating, _dragging;
    (double X, double Y) _last;
    double _pendingDx, _pendingDy;
    DateTime _lastDragSend;

    public MainWindow()
    {
        InitializeComponent();
        _pointer = new PointerPlanner(_settings.Pointer);
        _keyboard.CtrlAsCommand = _settings.CtrlAsCommand;

        Height = _settings.WindowHeight;
        if (!double.IsNaN(_settings.WindowLeft))
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Video.VideoAttached += OnVideoAttached;
        _controller.StateChanged += _ => Dispatcher.BeginInvoke(new Action(UpdateStatus));

        _overlay.MouseLeftButtonDown += Overlay_MouseDown;
        _overlay.MouseMove += Overlay_MouseMove;
        _overlay.MouseLeftButtonUp += Overlay_MouseUp;
        _overlay.MouseWheel += Overlay_MouseWheel;

        Loaded += OnLoaded;
        LocationChanged += (_, _) => PlaceOverlay();
        SizeChanged += (_, _) => PlaceOverlay();
        Video.SizeChanged += (_, _) => PlaceOverlay();
        Application.Current.Deactivated += (_, _) => ReleaseInput();
        _timer.Tick += (_, _) => Tick();
    }

    async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _overlay.Owner = this;
        _overlay.Show();
        PlaceOverlay();

        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(KeyHook);
        HwndSource.FromHwnd(new WindowInteropHelper(_overlay).Handle)?.AddHook(KeyHook);

        _uxplayExe = UxPlayProcess.Locate(_settings.UxPlayPath);
        FitWindowToVideo();
        Tick();
        _timer.Start();
        await ConnectControllerAsync();
    }

    async Task ConnectControllerAsync()
    {
        _connecting = true;
        _lastControllerTry = DateTime.UtcNow;
        if (await _controller.ConnectAsync(_settings.ComPort))
        {
            _settings.ComPort = _controller.PortName ?? "";
            _settings.Save();
        }
        _connecting = false;
        UpdateStatus();
    }

    // ---- video ---------------------------------------------------------------------

    void Tick()
    {
        if (_uxplayExe != null && !_uxplay.IsRunning && DateTime.UtcNow - _lastStart > TimeSpan.FromSeconds(5))
        {
            _lastStart = DateTime.UtcNow;
            _uxplay.Start(_uxplayExe, _settings.ReceiverName);
        }

        // The controller can be plugged in later; look for it every few seconds.
        if (_controller.State == ControllerState.NotFound && !_connecting &&
            DateTime.UtcNow - _lastControllerTry > TimeSpan.FromSeconds(10))
            _ = ConnectControllerAsync();

        Video.DropIfGone();
        if (!Video.HasVideo)
        {
            var hwnd = _uxplay.FindVideoWindow();
            if (hwnd != IntPtr.Zero)
            {
                Video.Attach(hwnd);
                _pointer.Invalidate();
            }
        }
        UpdateStatus();
    }

    void OnVideoAttached(int width, int height)
    {
        _videoW = width;
        _videoH = height;
        FitWindowToVideo();
    }

    void FitWindowToVideo()
    {
        double area = Video.ActualHeight;
        if (area <= 0) return;
        Width = Math.Max(MinWidth, ActualWidth - Video.ActualWidth + area * _videoW / _videoH);
    }

    void PlaceOverlay()
    {
        if (!_overlay.IsLoaded || PresentationSource.FromVisual(Video) is not { CompositionTarget: { } target }) return;
        var topLeft = target.TransformFromDevice.Transform(Video.PointToScreen(new Point(0, 0)));
        _overlay.Left = topLeft.X;
        _overlay.Top = topLeft.Y;
        _overlay.Width = Math.Max(1, Video.ActualWidth);
        _overlay.Height = Math.Max(1, Video.ActualHeight);
    }

    void UpdateStatus()
    {
        string video = _uxplayExe == null ? "UxPlay bulunamadı"
            : !_uxplay.IsRunning ? "UxPlay başlatılamadı" + (_uxplay.LastError is { } err ? $": {err}" : "")
            : Video.HasVideo ? "Görüntü geliyor"
            : $"Yansıtma bekleniyor (\"{_settings.ReceiverName}\")";
        string control = _controller.State switch
        {
            ControllerState.Paired => $"Kontrol hazır ({_controller.PortName})",
            ControllerState.Waiting => "Kontrolcü bulundu, iPhone'la eşleşmedi",
            _ => "Kontrolcü takılı değil (yalnızca izleme)",
        };
        StatusText.Text = $"{video}  ·  {control}";

        if (_calibrating) return;
        if (_uxplayExe == null)
            _overlay.ShowHint("Görüntü alıcısı (UxPlay) bulunamadı.\n\nuxplay klasörünü bu uygulamanın yanına koyun ya da ayarlar dosyasında UxPlayPath'i doldurun.");
        else if (!Video.HasVideo)
            _overlay.ShowHint($"iPhone'da Denetim Merkezi'ni açın → Ekran Yansıtma → \"{_settings.ReceiverName}\".\n\nBilgisayar ile iPhone aynı Wi-Fi ağında olmalı.");
        else
            _overlay.ShowHint(null);
    }

    // ---- mouse ---------------------------------------------------------------------

    (double X, double Y)? ScreenPoint(MouseEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        var box = VideoGeometry.Fit(_videoW, _videoH, _overlay.ActualWidth, _overlay.ActualHeight);
        return VideoGeometry.ToScreen(box, p.X, p.Y);
    }

    bool CanControl => Video.HasVideo && _controller.State == ControllerState.Paired;

    void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanControl || ScreenPoint(e) is not { } pt) return;

        if (_calibrating)
        {
            _calibrating = false;
            if (_pointer.FinishCalibration(pt.X, pt.Y)) _settings.Save();
            _overlay.ShowHint(null);
            return;
        }

        _controller.Send(_pointer.MoveTo(pt.X, pt.Y));
        _controller.Send(Protocol.Buttons(1));
        _dragging = true;
        _last = pt;
        _pendingDx = _pendingDy = 0;
        _overlay.CaptureMouse();
    }

    void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(_overlay);
        var box = VideoGeometry.Fit(_videoW, _videoH, _overlay.ActualWidth, _overlay.ActualHeight);
        var pt = (X: (p.X - box.X) / box.Width, Y: (p.Y - box.Y) / box.Height);

        _pendingDx += pt.X - _last.X;
        _pendingDy += pt.Y - _last.Y;
        _last = pt;
        if (DateTime.UtcNow - _lastDragSend < DragSendInterval) return;
        FlushDrag();
    }

    void FlushDrag()
    {
        _controller.Send(_pointer.MoveBy(_pendingDx, _pendingDy));
        _pendingDx = _pendingDy = 0;
        _lastDragSend = DateTime.UtcNow;
    }

    void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        FlushDrag();
        _controller.Send(Protocol.Buttons(0));
        _dragging = false;
        _overlay.ReleaseMouseCapture();
    }

    void Overlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!CanControl || ScreenPoint(e) is not { } pt) return;
        // Scrolling happens under the pointer, so bring it there first if it is far away.
        if (_pointer.Estimate is not { } at || Math.Abs(at.X - pt.X) > 0.05 || Math.Abs(at.Y - pt.Y) > 0.05)
            _controller.Send(_pointer.MoveTo(pt.X, pt.Y));
        _controller.Send(Protocol.Wheel(e.Delta / 40));
    }

    // ---- keyboard ------------------------------------------------------------------

    IntPtr KeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is not (Native.WM_KEYDOWN or Native.WM_KEYUP or Native.WM_SYSKEYDOWN or Native.WM_SYSKEYUP))
            return IntPtr.Zero;
        if (_controller.State != ControllerState.Paired) return IntPtr.Zero;

        long lp = lParam.ToInt64();
        int scan = (int)((lp >> 16) & 0xFF);
        bool extended = ((lp >> 24) & 1) != 0;
        bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;

        // Alt+F4 still closes the window.
        if (msg == Native.WM_SYSKEYDOWN && scan == 0x3E) return IntPtr.Zero;

        var report = down ? _keyboard.KeyDown(scan, extended) : _keyboard.KeyUp(scan, extended);
        if (report != null) _controller.Send(report);
        handled = true;
        return IntPtr.Zero;
    }

    void ReleaseInput()
    {
        if (_controller.State != ControllerState.Paired) return;
        _controller.Send(_keyboard.ReleaseAll());
        if (_dragging)
        {
            _controller.Send(Protocol.Buttons(0));
            _dragging = false;
        }
    }

    // ---- toolbar -------------------------------------------------------------------

    void Home_Click(object sender, RoutedEventArgs e) => _controller.Send(Protocol.Consumer(Protocol.Home));

    async void AppSwitcher_Click(object sender, RoutedEventArgs e)
    {
        _controller.Send(Protocol.Consumer(Protocol.Home));
        await Task.Delay(120);
        _controller.Send(Protocol.Consumer(Protocol.Home));
    }

    void Search_Click(object sender, RoutedEventArgs e)
    {
        _controller.Send(Protocol.Keyboard(0x08, [0x2C]));   // ⌘ + Space
        _controller.Send(Protocol.Keyboard(0, []));
    }

    void VolumeDown_Click(object sender, RoutedEventArgs e) => _controller.Send(Protocol.Consumer(Protocol.VolumeDown));
    void VolumeUp_Click(object sender, RoutedEventArgs e) => _controller.Send(Protocol.Consumer(Protocol.VolumeUp));

    void Rotate_Click(object sender, RoutedEventArgs e)
    {
        (_videoW, _videoH) = (_videoH, _videoW);
        FitWindowToVideo();
    }

    void Align_Click(object sender, RoutedEventArgs e) => _controller.Send(_pointer.Home());

    void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (!CanControl) return;
        _calibrating = true;
        _controller.Send(_pointer.StartCalibration());
        _overlay.ShowHint("Kalibrasyon: iPhone'daki gri imleç durduğunda tam üstüne tıklayın.", banner: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseInput();
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowHeight = Height;
        _settings.Save();
        _timer.Stop();
        _uxplay.Dispose();
        _controller.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}

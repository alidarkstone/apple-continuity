using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using AirPodsWin.Bluetooth;
using AirPodsWin.Core;

namespace AirPodsWin.UI;

public enum ConnectionState { Disconnected, Connecting, Connected, Failed, NotPaired, Pairing, PairFailed, ChoosingDevice }

/// <summary>
/// The iOS-style AirPods card: springs up from the bottom of the screen,
/// shows the batteries and offers a one-tap connect.
/// </summary>
public partial class PopupWindow : Window
{
    static readonly TimeSpan IdleDismiss = TimeSpan.FromSeconds(12);
    static readonly TimeSpan ConnectedDismiss = TimeSpan.FromSeconds(3);

    readonly DispatcherTimer _dismissTimer = new();
    ConnectionState _state;
    bool _closing;
    TaskCompletionSource<string?>? _choice;

    /// <summary>Raised when the user taps "Bağlan". The handler returns whether it worked.</summary>
    public Func<Task<bool>>? ConnectRequested { get; set; }

    /// <summary>Raised when the user taps "Windows ile eşleştir". Pairs and connects; returns whether it worked.</summary>
    public Func<Task<bool>>? PairRequested { get; set; }

    public PopupWindow()
    {
        InitializeComponent();
        _dismissTimer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => RestartDismissTimer();
        SourceInitialized += (_, _) => MakeNonActivating();
    }

    public bool IsShowing => IsVisible && !_closing;

    public void ShowCard(string title, ProximityPairingMessage message, ConnectionState state)
    {
        TitleText.Text = title;
        Update(message);
        SetState(state);

        if (IsShowing)
        {
            RestartDismissTimer();
            return;
        }

        PositionAtBottomCenter();
        Show();
        AnimateIn();
        RestartDismissTimer();
    }

    public void Update(ProximityPairingMessage message)
    {
        bool single = message.Model.IsSingleBattery();
        LeftBattery.Percent = single ? message.SingleBattery : message.LeftBattery;
        LeftBattery.Charging = message.LeftCharging;
        LeftBattery.Label = single ? "Pil" : "Sol";
        RightBattery.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        CaseBattery.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        RightBattery.Percent = message.RightBattery;
        RightBattery.Charging = message.RightCharging;
        CaseBattery.Percent = message.CaseBattery;
        CaseBattery.Charging = message.CaseCharging;

        // Pro models have short stems.
        double stem = message.Model.IsPro() ? 30 : 40;
        LeftStem.Height = stem;
        RightStem.Height = stem;
        CaseLed.Fill = new SolidColorBrush(message.CaseCharging || (message.CaseBattery ?? 100) > 20
            ? Color.FromRgb(0x30, 0xD1, 0x58)
            : Color.FromRgb(0xFF, 0x9F, 0x0A));
    }

    static bool IsBusy(ConnectionState state) => state is ConnectionState.Connecting or ConnectionState.Pairing;

    public void SetState(ConnectionState state)
    {
        _state = state;
        Spinner.Visibility = IsBusy(state) ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsHitTestVisible = !IsBusy(state) && state != ConnectionState.Connected;

        var blue = Color.FromRgb(0x0A, 0x84, 0xFF);
        var red = Color.FromRgb(0xFF, 0x45, 0x3A);
        (string text, Color color, string hint) = state switch
        {
            ConnectionState.Connecting => ("Bağlanıyor…", blue, ""),
            ConnectionState.Connected => ("Bağlandı  ✓", Color.FromRgb(0x30, 0xD1, 0x58), ""),
            ConnectionState.Failed => ("Tekrar dene", red, "Bağlanamadı. Kutu açık ve bilgisayara yakın olsun."),
            ConnectionState.NotPaired => ("Windows ile eşleştir", blue, "Bu AirPods bu bilgisayarla henüz eşleşmedi. Bir kez eşleştirmen yeterli."),
            ConnectionState.Pairing => ("Eşleştiriliyor…", blue, "Işık beyaz yanıp sönene kadar kutunun önüne iki kez dokun (AirPods Pro 3, AirPods 4) ya da arkadaki düğmeye basılı tut."),
            ConnectionState.ChoosingDevice => ("Vazgeç", Color.FromRgb(0x48, 0x48, 0x4A), "Yakında birden fazla cihaz var ya da AirPods henüz görünmedi. Hangisi senin AirPods'un?"),
            ConnectionState.PairFailed => ("Tekrar dene", red, "Eşleştirme olmadı. Kapak açıkken ışık beyaz yanıp sönene kadar kutunun önüne iki kez dokun ya da arkadaki düğmeye basılı tut."),
            _ => ("Bağlan", blue, ""),
        };
        ConnectText.Text = text;
        HintText.Text = hint;
        HintText.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AnimateButtonColor(color);

        bool choosing = state == ConnectionState.ChoosingDevice;
        DeviceList.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        ArtBox.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        BatteryRow.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        if (!choosing)
            _choice?.TrySetResult(null);
        else
            _dismissTimer.Stop();

        if (IsBusy(state))
        {
            _dismissTimer.Stop();
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        if (state == ConnectionState.Connected && IsShowing)
            RestartDismissTimer(ConnectedDismiss);
    }

    /// <summary>Shows the nearby devices in the card and waits for the user to pick one (null = cancelled).</summary>
    public Task<string?> ChooseDeviceAsync(IReadOnlyList<PairingCandidate> candidates)
    {
        _choice?.TrySetResult(null);
        var choice = new TaskCompletionSource<string?>();
        SetState(ConnectionState.ChoosingDevice);
        _choice = choice;
        ShowChoices(candidates);
        return choice.Task;
    }

    public void ShowChoices(IReadOnlyList<PairingCandidate> candidates)
    {
        if (_state != ConnectionState.ChoosingDevice)
            return;
        DeviceList.Children.Clear();
        if (candidates.Count == 0)
        {
            DeviceList.Children.Add(new TextBlock
            {
                Text = "Yakındaki cihazlar aranıyor…",
                Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }
        foreach (var c in candidates.Take(5))
        {
            var item = new Button
            {
                Content = c.Name,
                Style = (Style)FindResource("PrimaryButton"),
                Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8),
                Height = 44,
            };
            item.Click += (_, _) => _choice?.TrySetResult(c.Id);
            DeviceList.Children.Add(item);
        }
    }

    async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_state == ConnectionState.ChoosingDevice)
        {
            _choice?.TrySetResult(null);
            return;
        }
        if (IsBusy(_state))
            return;

        bool pairing = _state is ConnectionState.NotPaired or ConnectionState.PairFailed;
        var action = pairing ? PairRequested : ConnectRequested;
        if (action is null)
            return;

        SetState(pairing ? ConnectionState.Pairing : ConnectionState.Connecting);
        bool ok;
        try
        {
            ok = await action();
        }
        catch (Exception)
        {
            ok = false;
        }
        SetState(ok ? ConnectionState.Connected : pairing ? ConnectionState.PairFailed : ConnectionState.Failed);
        if (!ok)
            RestartDismissTimer();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

    public void Dismiss()
    {
        if (!IsShowing)
            return;
        _closing = true;
        _dismissTimer.Stop();
        _choice?.TrySetResult(null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(320);
        var slide = new DoubleAnimation(Height + 40, duration) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            Hide();
            _closing = false;
        };
        CardOffset.BeginAnimation(TranslateTransform.YProperty, slide);
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }

    void AnimateIn()
    {
        // Overshooting spring like the iOS sheet.
        var spring = new ElasticEase { Oscillations = 1, Springiness = 7, EasingMode = EasingMode.EaseOut };
        var smooth = new CubicEase { EasingMode = EasingMode.EaseOut };

        CardOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(Height + 40, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = spring });
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = smooth });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = smooth });

        // Pods pop in slightly after the card, then float gently.
        var pop = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(650))
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };
        ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        ArtFloat.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    void AnimateButtonColor(Color color)
    {
        if (ConnectButton.Background is not SolidColorBrush brush || brush.IsFrozen)
        {
            brush = new SolidColorBrush(color);
            ConnectButton.Background = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(250)));
    }

    void RestartDismissTimer(TimeSpan? after = null)
    {
        _dismissTimer.Stop();
        if (IsBusy(_state) || _state == ConnectionState.ChoosingDevice)
            return;
        _dismissTimer.Interval = after ?? IdleDismiss;
        _dismissTimer.Start();
    }

    void PositionAtBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 8;
    }

    void MakeNonActivating()
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
